using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace ConanProcessing
{
    public static class StringExtensions
    {
        public static List<string> RemoveAllOf(this List<string> list, Predicate<string> match)
        {
            list.RemoveAll(match);
            return list;
        }

        public static int Count(this string input, string substr)
        {
            int freq = 0;

            int index = input.IndexOf(substr);
            while (index >= 0)
            {
                index = input.IndexOf(substr, index + substr.Length);
                freq++;
            }
            return freq;
        }


        public static string Join(this List<string> list)
        {
            return String.Join("\r\n", list.ToArray());
        }
    }




    // Common Properties
    struct CommonMediaInfo
    {
        static readonly Dictionary<TrackType, int> cCodecPosition = new Dictionary<TrackType, int> {
                { TrackType.Video, 6 },
                { TrackType.Audio, 5 },
                { TrackType.Subtitle, 5 }
        };

        static readonly Dictionary<TrackType, int> cLanguagePosition = new Dictionary<TrackType, int> {
                { TrackType.Video, 7 },
                { TrackType.Audio, 6 },
                { TrackType.Subtitle, 6 }
        };

        public enum TrackType
        {
            Video, Audio, Subtitle
        }

        public CommonMediaInfo(TrackType aType, List<string> aTrackPropset)
        {
            TrackNumber = System.Int32.Parse(aTrackPropset[0].Split(' ')[9].Split(')')[0]);
            TrackCodec = GetCodec(aType, aTrackPropset);
            Language = GetLanguage(aType, aTrackPropset);
        }

        static string GetCodec(TrackType aType, List<string> aTrackPropset)
        {
            return aTrackPropset[cCodecPosition[aType]].Split(' ')[2].Trim().Substring(2);
        }

        static string GetLanguage(TrackType aType, List<string> aTrackPropset)
        {
            return aTrackPropset[cLanguagePosition[aType]].Split(' ')[1].Trim();
        }

        public int TrackNumber;
        public string TrackCodec;
        public string Language;
    }

    struct VideoInfo
    {
        public VideoInfo(List<string> aTrackPropset)
        {
            mMediaInfo = new CommonMediaInfo(CommonMediaInfo.TrackType.Video, aTrackPropset);
        }

        public CommonMediaInfo mMediaInfo;
    }

    struct AudioInfo
    {
        public AudioInfo(List<string> aTrackPropset)
        {
            mMediaInfo = new CommonMediaInfo(CommonMediaInfo.TrackType.Audio, aTrackPropset);
        }

        public CommonMediaInfo mMediaInfo;
    }

    struct SubtitleInfo
    {
        public SubtitleInfo(List<string> aTrackPropset)
        {
            mMediaInfo = new CommonMediaInfo(CommonMediaInfo.TrackType.Subtitle, aTrackPropset);
        }

        public CommonMediaInfo mMediaInfo;
    }

    struct ChapterInfo
    {
        public ChapterInfo(string aTitle, string aStart, string aEnd, int aNumber)
        {
            var startParts = aStart.Split(':');
            var endParts = aEnd.Split(':');

            float test = float.Parse(startParts[0]);
            //float.Parse(startParts[0]), float.Parse(startParts[1]), float.Parse(startParts[2]);
            Title = aTitle;
            Start = new TimeSpan(0, (int)float.Parse(startParts[0]), (int)float.Parse(startParts[1]), (int)float.Parse(startParts[2]));
            End = new TimeSpan(0, (int)float.Parse(endParts[0]), (int)float.Parse(endParts[1]), (int)float.Parse(endParts[2]));
            Number = aNumber;
        }

        public ChapterInfo(string aTitle, long aStartTicks, long aEndTicks, int aNumber)
        {
            Title = aTitle;
            Start = new TimeSpan(aStartTicks);
            End = new TimeSpan(aEndTicks);
            Number = aNumber;
        }

        public string Title;
        public TimeSpan Start;
        public TimeSpan End;
        public int Number;
    }


    class MkvInfo
    {
        public MkvInfo(string aFile, List<string> aTracks, List<string> aChapters)
        {
            mVideoInfos = new List<VideoInfo>();
            mAudioInfos = new List<AudioInfo>();
            mSubtitleInfos = new List<SubtitleInfo>();
            mChapters = new List<ChapterInfo>();
            File = aFile;

            foreach (var track in aTracks)
            {
                // Get Track properties
                var properties = track.Split("|  + ").ToList();
                properties.RemoveAt(0); //Empty Line
            
                if (properties[2].StartsWith("Track type: video"))
                    mVideoInfos.Add(new VideoInfo(properties));
                else if (properties[2].StartsWith("Track type: audio"))
                    mAudioInfos.Add(new AudioInfo(properties));
                else if (properties[2].StartsWith("Track type: subtitles"))
                    mSubtitleInfos.Add(new SubtitleInfo(properties));
            }

            // Chapters start at 1 for the sake of splitting, so we'll do that here too.
            int chapterNumber = 1;

            foreach (var chapter in aChapters)
            {
                var lines =  chapter.ReplaceLineEndings().Split(Environment.NewLine);

                string title = "";
                string start = "";
                string end = "";

                foreach (var line in lines)
                {
                    if (line.StartsWith("|   + Chapter time start: "))
                        start = line.Substring("|   + Chapter time start: ".Length);
                    else if (line.StartsWith("|   + Chapter time end: "))
                        end = line.Substring("|   + Chapter time end: ".Length);
                    else if (line.StartsWith("|    + Chapter string: "))
                        title = line.Substring("|    + Chapter string: ".Length);
                }

                if ((0 != title.Length) && (0 != start.Length) && (0 != end.Length)) 
                {
                    mChapters.Add(new ChapterInfo(title, start, end, chapterNumber));
                    ++chapterNumber;
                }
            }
        }

        public static MkvInfo GetMkvInfo(string aFile)
        {
            var builder = new StringBuilder();
            var process = Program.RunProcess("mkvinfo", aFile, builder);
            process.WaitForExit();

            var mkvinfoOutput = builder
                .ToString()
                .Split("\r\n")
                .ToList();

            var mkvinfoOutputScrubbed = mkvinfoOutput
                .RemoveAllOf(line => line.StartsWith("| + EBML void: "))
                .Join()
                .Split("|+ ");

            var tracks = mkvinfoOutputScrubbed
                .First(line => line.StartsWith("Tracks"))
                .Split("| + Track\r\n")
                .ToList();


            var chapters = mkvinfoOutputScrubbed
                .First(line => line.StartsWith("Chapters"))
                .Split("|  + Chapter atom\r\n")
                .ToList();

            // First one is just the beginning of the track section.
            tracks.RemoveAt(0);

            return new MkvInfo(aFile, tracks, chapters);
        }

        public List<VideoInfo> mVideoInfos;
        public List<AudioInfo> mAudioInfos;
        public List<SubtitleInfo> mSubtitleInfos;
        public List<ChapterInfo> mChapters;
        public string File;
    }





















    public struct Episode
    {
        public Episode(string aEpisodeNumber, string aTitle, bool aOneHourSpecial, bool aTwoHourSpecial, bool aTwoPointFiveHourSpecial)
        {
            EpisodeNumber = aEpisodeNumber;
            Title = aTitle;
            OneHourSpecial = aOneHourSpecial;
            TwoHourSpecial = aTwoHourSpecial;
            TwoPointFiveHourSpecial = aTwoPointFiveHourSpecial;
        }

        public string EpisodeNumber;
        public string Title;
        public bool OneHourSpecial = false;
        public bool TwoHourSpecial = false;
        public bool TwoPointFiveHourSpecial = false;
    }

    public struct Disk
    {
        public Disk()
        {
            Episodes = new List<Episode>();
        }

        public List<Episode> Episodes;
    }

    public struct Part
    {
        public Part()
        {
            Disks = new Dictionary<Int32, Disk>();
        }

        public Dictionary<Int32, Disk> Disks;
    }

    internal class Program
    {
        public static Process RunProcess(string aProgram, string aCommandLine, StringBuilder? stringBuilder = null)
        {
            Console.WriteLine($"{aProgram} {aCommandLine}");

            var process = new Process();
            process.StartInfo.FileName = aProgram;
            process.StartInfo.Arguments = aCommandLine;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;

            if (null != stringBuilder)
                process.OutputDataReceived += (sender, args) => stringBuilder.AppendLine(args.Data);
            else
                process.OutputDataReceived += (sender, args) => Console.WriteLine("received output: {0}", args.Data);

            process.Start();
            process.BeginOutputReadLine();
            return process;
        }

        static Dictionary<Int32, Part> ParseParts(Dictionary<(string, bool), (bool, bool, bool)> aEpisodeAndRemasterToSpecialStatus)
        {
            Dictionary<Int32, Part> parts = new Dictionary<Int32, Part>();
            using (var reader = new StreamReader(@"DVD_and_disk_to_episode_guide.csv"))
            {
                // Throw away the first line
                if (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                }

                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    var values = line.Split(',', 4);

                    Int32 partNumber = Int32.Parse(values[0]);
                    Int32 discNumber = Int32.Parse(values[1]);


                    string title = values[3];

                    if (!parts.ContainsKey(partNumber))
                    {
                        parts.Add(partNumber, new Part());
                    }

                    Part part = parts[partNumber];

                    if (!part.Disks.ContainsKey(discNumber))
                    {
                        part.Disks.Add(discNumber, new Disk());
                    }

                    Disk disk = part.Disks[discNumber];
                    
                    var episodeNumber = values[2];

                    var (oneHourSpecial, twoHourSpecial, twoPointFiveHourSpecial) = aEpisodeAndRemasterToSpecialStatus[(episodeNumber, false)];

                    disk.Episodes.Add(new Episode(episodeNumber, title, oneHourSpecial, twoHourSpecial, twoPointFiveHourSpecial));
                }
            }

            return parts;
        }

        static void RipMkv()
        {
            //"C:\Program Files\MakeMKV\makemkvcon64.exe" mkv iso:"\\192.168.0.130\VIDEOS\ISO\MyMovie.iso" all "\\192.168.0.130\VIDEOS\ISO\MyMovie\"

            //
            Dictionary<string, string> disksToRip = new Dictionary<string, string>();
            foreach (var partDir in Directory.GetDirectories("H:/DetectiveConan/Raws"))
            {
                foreach (var diskIso in Directory.GetFiles(partDir))
                {
                    string japaneseTitle = "[DVDISO][アニメ] 名探偵コナン ";
                    var sub = Path.GetFileNameWithoutExtension(diskIso).Substring(japaneseTitle.Length);

                    disksToRip.Add(diskIso, $"H:/DetectiveConan/Rips/{sub}");
                }
            }

            foreach(var disk in disksToRip)
            {
                Directory.CreateDirectory(disk.Value);
                var proc = RunProcess("C:/Program Files (x86)/MakeMKV/makemkvcon64.exe", $"mkv iso:\"{disk.Key}\" all \"{disk.Value}\"");
                proc.WaitForExit();
            }
        }


        static void MoveAndRenameMkv()
        {

            foreach (var seasonDir in Directory.GetDirectories("H:/DetectiveConan/Rips/"))
            {
                var mkvs = Directory.GetFiles(seasonDir);

                if (1 != mkvs.Length)
                {
                    Console.WriteLine($"{seasonDir}, {mkvs.Length}");

                    foreach (var mkv in mkvs)
                    {
                        var fi1 = new FileInfo(mkv);
                        Console.WriteLine($"\t{mkv}, {fi1.Length / (1024.0f * 1024.0f)}");
                    }
                    continue;
                }
            }
        }

        static Dictionary<(int, int), MkvInfo> WriteChaptersPerDisc()
        {
            //Dictionary<string, long> discToChapters = new Dictionary<string, long>();

            Dictionary<(int, int), MkvInfo> chapterTimes = new Dictionary<(int, int), MkvInfo>();
            foreach (var seasonDir in Directory.GetDirectories("H:/DetectiveConan/Rips/"))
            {
                
                var mkvs = Directory.GetFiles(seasonDir);

                if (0 == mkvs.Length)
                {
                    Console.WriteLine($"{seasonDir} failed to find mkv");
                    continue;
                }


                string mkv = mkvs.MaxBy(f =>
                {
                    return new FileInfo(f).Length;
                }) ?? "";


                if (mkv.Equals(""))
                {
                    Console.WriteLine($"{seasonDir} failed to find max mkv");
                    continue;
                }

                var info = MkvInfo.GetMkvInfo(Path.Combine(seasonDir, mkv));
                var partAndDisc = Path.GetFileNameWithoutExtension(seasonDir).Replace("-DragonMax", "").Split('-', 2);
                
                Int32 partNumber = Int32.Parse(partAndDisc[0]);
                Int32 discNumber = Int32.Parse(partAndDisc[1]);
                chapterTimes.Add((partNumber, discNumber), info);
            }

            StringBuilder builder = new StringBuilder();

            foreach (var ((partNumber, diskNumber), mkvInfo) in chapterTimes)
            {
                foreach (var chapterInfo in mkvInfo.mChapters)
                {
                    builder.AppendLine($"{partNumber}:{diskNumber}:{chapterInfo.Start.Ticks}:{chapterInfo.End.Ticks}:{chapterInfo.Number}:{mkvInfo.File}:{chapterInfo.Title}");
                }
            }

            File.WriteAllText("ChaptersPerDisc.csv", builder.ToString());

            return chapterTimes;
        }

        struct MkvChapterInfo
        {
            public MkvChapterInfo(string aMkvFile)
            {
                MkvFile = aMkvFile;
                Chapters = new List<ChapterInfo>();
            }

            public string MkvFile;
            public List<ChapterInfo> Chapters;
        }


        static Dictionary<(int, int), MkvChapterInfo> ReadChaptersPerDisc()
        {
            Dictionary<(int, int), MkvChapterInfo> discsAndChapter = new Dictionary<(int, int), MkvChapterInfo>();
            
            using (var reader = new StreamReader("ChaptersPerDisc.csv"))
            {
                while (!reader.EndOfStream)
                {
                    var values = reader.ReadLine().Split(':', 7);
            
                    Int32 partNumber = Int32.Parse(values[0]);
                    Int32 discNumber = Int32.Parse(values[1]);
                    long startTicks = long.Parse(values[2]);
                    long endTicks = long.Parse(values[3]);
                    Int32 chapterNumber = Int32.Parse(values[4]);
                    string mkvFile = values[5];
                    string chapterTitle = values[6];

                    if (!discsAndChapter.ContainsKey((partNumber, discNumber)))
                    {
                        discsAndChapter.Add((partNumber, discNumber), new MkvChapterInfo(mkvFile));
                    }

                    discsAndChapter[(partNumber, discNumber)].Chapters.Add(new ChapterInfo(chapterTitle, startTicks, endTicks, chapterNumber));
                }
            }
            
            return discsAndChapter;
        }

        static Dictionary<(string, bool), (bool, bool, bool)> ProcessEpisodeGuide()
        {
            //Console.WriteLine(File.ReadAllText("Test.txt").Replace("\r\n(", "("));
            //File.WriteAllText("Test2.txt", File.ReadAllText("Test.txt").Replace("\r\n(", "("));

            //StringBuilder test = new StringBuilder();

            Dictionary<(string, bool), (bool, bool, bool)> episodeAndRemasterToSpecialStatus = new Dictionary<(string, bool), (bool, bool, bool)>();

            using (var reader = new StreamReader(@"EpisodeGuide.csv"))
            {
                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    var values = line.Split('\t', StringSplitOptions.None);

                    string episodeNumber = values[0];
                    string internationalEpisodeNumber = values[1];
                    string title = values[2];
                    string originalBroadcastDate = values[3];
                    string englishBroadcastDate = values[4];
                    //string englishDubBroadcastDate = values[5];
                    //string plotInfoDate = values[6];
                    //string mangaSource = values[7];
                    //string nextConansHint = values[8];

                    if (title.Contains("(Rerun)") || title.Contains("(Remastered)[2]") || title.Contains("(Remastered)[3]") || title.Contains("(Remastered - "))
                    {
                        continue;
                    }

                    bool isRemastered = title.Contains("(Remastered)");
                    bool is1HourSpecial = title.Contains("(1 Hour Special)");
                    bool is2HourSpecial = title.Contains("(2 Hour Special)");
                    bool is25HourSpecial = title.Contains("(2.5 Hour Special)");

                    if (episodeAndRemasterToSpecialStatus.ContainsKey((episodeNumber, isRemastered)))
                    {
                        Console.WriteLine($"Maybe \'{episodeNumber};{title}\' is an unmarked rerun? Skipping");
                        continue;
                    }

                    episodeAndRemasterToSpecialStatus.Add((episodeNumber, isRemastered), (is1HourSpecial, is2HourSpecial, is25HourSpecial)); 
                }
            }

            return episodeAndRemasterToSpecialStatus;

            //File.WriteAllText("test4.txt", test.ToString());
        }

        static Dictionary<string, (int, int, string)> ParseSubtitles()
        {
            Dictionary<string, (int, int, string)> episodeToSubtitleStartEnd = new Dictionary<string, (int, int, string)>();
            foreach (var assFile in Directory.GetFiles("conanSubs/subs/0001-0999"))
            {
                var parser = new SubtitlesParser.Classes.Parsers.SubParser();
                using (var fileStream = File.OpenRead(assFile))
                {
                    var items = parser.ParseStream(fileStream);


                    var min = items.MinBy(f => f.StartTime);
                    var max = items.MaxBy(f => f.EndTime);
                    episodeToSubtitleStartEnd.Add(Path.GetFileNameWithoutExtension(assFile).TrimStart('0'), (min.StartTime, max.EndTime, assFile));
                }
            }

            return episodeToSubtitleStartEnd;
        }

        struct DiskAndSource
        {
            public DiskAndSource(int aPart, int aDisk, string aFile)
            {
                Part = aPart;
                Disk = aDisk;
                File = aFile;
            }

            int Part;
            int Disk;
            string File;
        }

        struct ChapterToEpisodeInfo
        {
            public ChapterToEpisodeInfo(string aEpisodeNumber, string aEpisodeTitle, int aChapterStart, int aChapterEnd)
            {
                EpisodeNumber = aEpisodeNumber;
                EpisodeTitle = aEpisodeTitle;
                ChapterStart = aChapterStart;
                ChapterEnd = aChapterEnd;
            }

            public string EpisodeNumber;
            public string EpisodeTitle;
            public int ChapterStart;
            public int ChapterEnd;
        }

        static Dictionary<(int,int, string), List<ChapterToEpisodeInfo>> DiscsToChaptersToEpisodes(Dictionary<Int32, Part> aParts, Dictionary<string, (int, int, string)> aEpisodeToSubtitleStartEnd, Dictionary<(int, int), MkvChapterInfo> aChaptersPerDisc)
        {
            Dictionary<(int, int, string), List<ChapterToEpisodeInfo>> info = new Dictionary<(int, int, string), List<ChapterToEpisodeInfo>>();

            foreach (var (partNumber, part) in aParts)
            {
                foreach (var (discNumber, disc) in part.Disks)
                {
                    if (!aChaptersPerDisc.ContainsKey((partNumber, discNumber)))
                    {
                        Console.WriteLine($"No chapter info for Part {partNumber}, Disc {partNumber}");
                        continue;
                    }

                    var chapterInfoOnDisc = aChaptersPerDisc[(partNumber, discNumber)];

                    // Perpare info for this disc.
                    info.Add((partNumber, discNumber, chapterInfoOnDisc.MkvFile), new List<ChapterToEpisodeInfo>());
                    var discInfo = info[(partNumber, discNumber, chapterInfoOnDisc.MkvFile)];

                    // Single Episode Discs can have as many chapters as they like.
                    if (1 == disc.Episodes.Count())
                    {
                        var episode = disc.Episodes.First();
                        discInfo.Add(new ChapterToEpisodeInfo(episode.EpisodeNumber, episode.Title, 1, chapterInfoOnDisc.Chapters.Last().Number + 1));
                        continue;
                    }

                    var lastChapterEndTime = new TimeSpan(0, 0, 0, 0);
                    var currentChapterStart = 1;

                    foreach (var episode in disc.Episodes)
                    {
                        var (episodeStartTime, episodeEndTime, assFile) = aEpisodeToSubtitleStartEnd[episode.EpisodeNumber];
                        var episodeEndTimeOnDisc = new TimeSpan(0, 0, 0, 0, episodeEndTime) + lastChapterEndTime;

                        var endChapter = chapterInfoOnDisc.Chapters.MinBy(f =>
                        {
                            var timeBetween = f.End - episodeEndTimeOnDisc;
                            return (timeBetween.Ticks < 0) ? TimeSpan.MaxValue : timeBetween;
                        });

                        if (chapterInfoOnDisc.Chapters.Last().End < episodeEndTimeOnDisc)
                        {
                            Console.WriteLine($"Not enough time: Detective Conan - {episode.EpisodeNumber} - {episode.Title}");
                        }


                        discInfo.Add(new ChapterToEpisodeInfo(episode.EpisodeNumber, episode.Title, currentChapterStart, endChapter.Number));
                        currentChapterStart = endChapter.Number + 1;
                        lastChapterEndTime = endChapter.End;
                    }
                }
            }

            return info;
        }

        static void Main(string[] args)
        {

            Console.WriteLine(Directory.GetCurrentDirectory());

            var episodeAndRemasterToSpecialStatus = ProcessEpisodeGuide();
            var parts = ParseParts(episodeAndRemasterToSpecialStatus);
            if (!File.Exists("ChaptersPerDisc.csv"))
            {
                WriteChaptersPerDisc();
            }

            var chaptersPerDisc = ReadChaptersPerDisc();
            var episodeToSubtitleStartEnd = ParseSubtitles();
            var discsToChaptersToEpisodes = DiscsToChaptersToEpisodes(parts, episodeToSubtitleStartEnd, chaptersPerDisc);

            StringBuilder builder = new StringBuilder();
            List<string> commands = new List<string>();


            // Detect episode titles with problematic characters in them
            //foreach (var (partNumber, part) in parts)
            //{
            //    foreach (var (discNumber, disc) in part.Disks)
            //    {
            //        foreach (var episodeName in disc.Episodes)
            //        {
            //            foreach (var c in Path.GetInvalidFileNameChars())
            //            {
            //                if (episodeName.Title.Contains(c))
            //                {
            //                    Console.WriteLine($"{episodeName.Title} contains {c}");
            //                }
            //            }
            //        }
            //    }
            //}


            /* 
            foreach (var ((partNumber, discNumber, file), discInfo) in discsToChaptersToEpisodes)
            {
                var discFolder = Path.Combine("E:/DetectiveConan/Temp", $"{partNumber}-{discNumber}");
            
                commands.Add("--output");
                commands.Add(Path.Combine(discFolder, "output.mkv"));
                commands.Add("--split");
                
                builder.Append("chapters:");
                
                foreach (var episode in discInfo)
                {
                    if (episode.ChapterStart != 1)
                    {
                        builder.Append($",");
                    }
                    builder.Append($"{episode.ChapterStart}");
                }
                commands.Add(builder.ToString());
                builder.Clear();
                commands.Add(file);
                
                Console.Write($"mkvmerge");
                foreach(var command in commands)
                {
                    builder.Append($" ");
                    builder.Append(command);
                }
                Console.Write($"\n");
                commands.Clear();
            
                RunProcess($"mkvmerge", builder.ToString()).WaitForExit();
                builder.Clear();
            
                var index = 0;
                var disc = parts[partNumber].Disks[discNumber];
            
                foreach (var tempOutputFile in Directory.GetFiles(discFolder).OrderBy(f => f))
                {
                    var episodeInfo = disc.Episodes[index];
                    var episodeName = $"Detective Conan - {episodeInfo.EpisodeNumber} - {episodeInfo.Title}";
                    var episodeFileName = episodeName.Replace(':', '∶');
                    
                    var episodePath = Path.Combine("E:/DetectiveConan", $"{episodeName}.mkv");
                    Console.WriteLine(episodeName);
            
            
                    RunProcess($"mkvmerge", $"-o \"{episodePath}\" \"{tempOutputFile}\" \"{episodeToSubtitleStartEnd[episodeInfo.EpisodeNumber].Item3}\"").WaitForExit();
                    RunProcess($"mkvpropedit", $"\"{episodePath}\" -e info -s title=\"{episodeName}\"").WaitForExit();
                    
                    ++index;
                    commands.Clear();
                }
            }

            */
        }
    }
}