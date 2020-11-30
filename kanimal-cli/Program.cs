using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using CommandLine;
using kanimal;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace kanimal_cli
{
    public class Program
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private static LoggingConfiguration GetLoggerConfig(ProgramOptions o)
        {
            var config = new LoggingConfiguration();
            var targetConsole = new ConsoleTarget("logconsole") {Layout = "[${level}] ${message}"};

            if (o.Verbose && o.Silent)
            {
                Console.WriteLine("You can't mix -v/--verbose and -s/--silent.");
                Environment.Exit((int) ExitCodes.IncorrectArguments);
            }

            if (o.Verbose)
                config.AddRule(LogLevel.Debug, LogLevel.Fatal, targetConsole);
            else if (o.Silent)
                config.AddRule(LogLevel.Error, LogLevel.Fatal, targetConsole);
            else
                config.AddRule(LogLevel.Info, LogLevel.Fatal, targetConsole);

            return config;
        }

        private static void SetVerbosity(ProgramOptions o)
        {
            LogManager.Configuration = GetLoggerConfig(o);
        }

        private static void Convert(string inputFormat, string outputFormat, List<string> files, ConversionOptions opt)
        {
            SetVerbosity(opt);

            if (files.Count == 0)
            {
                Logger.Fatal("Please specify files to convert.");
                Environment.Exit((int) ExitCodes.IncorrectArguments);
            }

            Logger.Info("Reading...");
            Reader reader = null;
            switch (inputFormat)
            {
                case "scml":
                    var scml = files.Find(path => path.EndsWith(".scml"));
                    var scmlreader = new ScmlReader(scml)
                    {
                        AllowMissingSprites = !opt.Strict,
                        AllowInFramePivots = !opt.Strict,
                        InterpolateMissingFrames = opt.Interp,
                        Debone = opt.Debone
                    };
                    scmlreader.Read();
                    reader = scmlreader;
                    break;
                case "kanim":
                    var png = "";
                    var build = "";
                    var anim = "";
                    if (opt is GenericOptions && ((GenericOptions)opt).Ordered)
                    {
                        png = files[0];
                        build = files[1];
                        anim = files[2];
                    }
                    else
                    {
                        png = files.Find(path => path.EndsWith(".png"));
                        build = files.Find(path => path.EndsWith("build.bytes"));
                        anim = files.Find(path => path.EndsWith("anim.bytes")); 
                    }

                    var fileNames = new[] {png, build, anim};

                    var nullCount = fileNames.Count(o => o == null);
                    if (nullCount > 0)
                    {
                        Logger.Fatal($"The following file{(nullCount > 1 ? "s were" : "was")} not specified:");
                        for (var i = 0; i < 3; ++i)
                            if (fileNames[i] == null)
                                switch (i)
                                {
                                    case 0:
                                        Logger.Fatal("    png");
                                        break;
                                    case 1:
                                        Logger.Fatal("    build");
                                        break;
                                    case 2:
                                        Logger.Fatal("    anim");
                                        break;
                                }

                        Environment.Exit((int) ExitCodes.IncorrectArguments);
                    }
                    reader = new KanimReader(
                        new FileStream(build, FileMode.Open),
                        new FileStream(anim, FileMode.Open),
                        new FileStream(png, FileMode.Open));
                    reader.Read();
                    break;
                default:
                    Logger.Fatal($"The specified input format \"{inputFormat}\" is not recognized.");
                    Environment.Exit((int) ExitCodes.IncorrectArguments);
                    break;
            }

            Logger.Info($"Successfully read from format {inputFormat}.");
            Logger.Info("Writing...");

            switch (outputFormat)
            {
                case "scml":
                    var scmlWriter = new ScmlWriter(reader)
                    {
                        FillMissingSprites = !opt.Strict,
                        AllowDuplicateSprites = !opt.Strict
                    };
                    scmlWriter.SaveToDir(Path.Join(opt.OutputPath));
                    break;
                case "kanim":
                    var kanimWriter = new KanimWriter(reader);
                    kanimWriter.SaveToDir(opt.OutputPath);
                    break;
                default:
                    Logger.Fatal($"The specified output format \"{outputFormat}\" is not recognized.");
                    Environment.Exit((int) ExitCodes.IncorrectArguments);
                    break;
            }

            Logger.Info($"Successfully wrote to format {outputFormat}");
        }

        private static void ConvertAnim(Object stateInfo)
        {
            ConversionData data = (ConversionData) stateInfo;
            var png = new FileStream(data.filePath, FileMode.Open);
            var anim = new FileStream(data.animPath, FileMode.Open);
            var build = new FileStream(data.buildPath, FileMode.Open);

            bool failed = true;
            var reader = new KanimReader(build, anim, png);
            try
            {
                reader.Read();
                var writer = new ScmlWriter(reader);
                writer.SaveToDir(Path.Join(data.outputPath, reader.BuildData.Name));
                failed = false;
            }
            catch (Exception e)
            {
                Logger.Error($"The following error occured while exporting \"{reader.BuildData.Name}\":");
                Logger.Error(e.ToString());
                Logger.Error("Skipping.");
            }

            if (!failed)
            {
                Logger.Info($"Exported \"{reader.BuildData.Name}\".");
            }
            Interlocked.Decrement(ref Counter);
            Interlocked.Increment(ref Success);
        }

        private static int Counter;
        private static int Success;

        private static void Main(string[] args)
        {
            Parser.Default
                .ParseArguments<KanimToScmlOptions, ScmlToKanimOptions, GenericOptions, DumpOptions, BatchConvertOptions
                >(args)
                .WithParsed<KanimToScmlOptions>(o => Convert(
                    "kanim",
                    "scml",
                    o.Files.ToList(),
                    o))
                .WithParsed<DumpOptions>(o =>
                {
                    SetVerbosity(o);

                    var files = new List<string>(o.Files);
                    var png = files.Find(path => path.EndsWith(".png"));
                    var build = files.Find(path => path.EndsWith("build.bytes"));
                    var anim = files.Find(path => path.EndsWith("anim.bytes"));
                    Directory.CreateDirectory(o.OutputPath);
                    Utilities.Dump =
                        new StreamWriter(new FileStream(Path.Join(o.OutputPath, "dump.log"), FileMode.Create));
                    var reader = new KanimReader(
                        new FileStream(build, FileMode.Open),
                        new FileStream(anim, FileMode.Open),
                        new FileStream(png, FileMode.Open));
                    reader.Read();
                    Utilities.Dump.Flush();
                })
                .WithParsed<ScmlToKanimOptions>(o => Convert(
                    "scml",
                    "kanim",
                    o.ScmlFile == null ? new List<string>() : new List<string> {o.ScmlFile},
                    o))
                .WithParsed<GenericOptions>(o => Convert(o.InputFormat, o.OutputFormat, o.Files.ToList(), o))
                .WithParsed<BatchConvertOptions>(o =>
                {
                    // Silence Info output from kanimal
                    var config = new LoggingConfiguration();
                    var target = new ConsoleTarget("logconsole") {Layout = "[${level}] ${message}"};
                    var loggingRule1 = new LoggingRule("kanimal_cli.*", target);
                    loggingRule1.SetLoggingLevels(LogLevel.Info, LogLevel.Fatal);
                    config.LoggingRules.Add(loggingRule1);
                    var loggingRule2 = new LoggingRule("kanimal.*", target);
                    loggingRule2.SetLoggingLevels(LogLevel.Warn, LogLevel.Fatal);
                    config.LoggingRules.Add(loggingRule2);
                    LogManager.Configuration = config;

                    if (!Directory.Exists(Path.Join(o.AssetDirectory, "Texture2D")))
                    {
                        Logger.Fatal($"The path \"{o.AssetDirectory}/Texture2D\" does not exist.");
                        Environment.Exit((int) ExitCodes.IncorrectArguments);
                    }

                    if (!Directory.Exists(Path.Join(o.AssetDirectory, "TextAsset")))
                    {
                        Logger.Fatal($"The path \"{o.AssetDirectory}/TextAsset\" does not exist.");
                        Environment.Exit((int) ExitCodes.IncorrectArguments);
                    }

                    Counter = 0;
                    Success = 0;
                    var filepaths = Directory.GetFiles(Path.Join(o.AssetDirectory, "Texture2D"), "*.png");
                    foreach (var filepath in filepaths)
                    {
                        var filename = Path.GetFileName(filepath);
                        var basename = Utilities.GetSpriteBaseName(filename);
                        if (basename == "")
                        {
                            Logger.Warn($"Skipping \"{filename}\" as it does not seem to be a valid anim.");
                            continue;
                        }

                        var animPath = Path.Join(o.AssetDirectory, "TextAsset", $"{basename}_anim.bytes");
                        var buildPath = Path.Join(o.AssetDirectory, "TextAsset", $"{basename}_build.bytes");
                        if (!File.Exists(animPath))
                        {
                            animPath = Path.Join(o.AssetDirectory, "TextAsset", $"{basename}_anim.txt");
                            if (!File.Exists(animPath))
                            {
                                animPath = Path.Join(o.AssetDirectory, "TextAsset", $"{basename}_anim.prefab");
                                if (!File.Exists(animPath))
                                {
                                    Logger.Warn(
                                    $"Skipping \"{basename}\" because it does not have a corresponding anim.bytes file.");
                                    continue;
                                }
                            }
                        }

                        if (!File.Exists(buildPath))
                        {
                            buildPath = Path.Join(o.AssetDirectory, "TextAsset", $"{basename}_build.txt");
                            if (!File.Exists(buildPath))
                            {
                                buildPath = Path.Join(o.AssetDirectory, "TextAsset", $"{basename}_build.prefab");
                                if (!File.Exists(buildPath))
                                {
                                    Logger.Warn(
                                    $"Skipping \"{basename}\" because it does not have a corresponding build.bytes file.");
                                    continue;
                                }
                            }
                        }

                        Interlocked.Increment(ref Counter);
                        ThreadPool.QueueUserWorkItem(ConvertAnim, new ConversionData(filepath, animPath, buildPath, o.OutputPath));
                    }

                    while (Counter > 0)
                    {
                        Thread.Sleep(100);
                    }
                    Logger.Info($"Finished exporting {Success} of {filepaths.Length} anims.");
                });
        }
    }
}