﻿using DBDefsLib;
using MPQToTACT;
using MPQToTACT.MPQ;
using MPQToTACT.Readers;
using System.Diagnostics;
using System.Formats.Tar;

namespace WoWTools.MinimapExtractMPQ
{
    internal class Program
    {
        static void Main(string[] args)
        {
            if (!Directory.Exists("work"))
                Directory.CreateDirectory("work");

            if (!File.Exists("MPQEditor.exe"))
                throw new Exception("MPQEditor.exe not found");

            var outDir = "M:\\Minimaps\\RawTiles";
            if (!Directory.Exists(outDir))
                Directory.CreateDirectory(outDir);

            var doneBuilds = new List<string>();

            var targetBuilds = new List<(Build build, string directory)>();

            foreach (var directory in Directory.GetDirectories("E:\\WoWArchive-0.X-3.X\\Mount"))
            {
                var dirOnly = Path.GetFileName(directory);
                var parts = dirOnly.Split('_');
                var build = parts[^1];

                if (build.Contains('-'))
                    continue;

                if (directory.Contains("_PTR_"))
                {
                    var splitBuild = build.Split('.');
                    build = dirOnly[0] + "." + splitBuild[1] + "." + splitBuild[2] + "." + splitBuild[3];
                }

                targetBuilds.Add((new Build(build), directory));
            }

            // Sort as we expect builds to be sorted
            targetBuilds.Sort();

            foreach (var targetBuild in targetBuilds)
            {
                var directory = targetBuild.directory;
                var build = targetBuild.build.ToString();
                var branch = directory.Split('_')[1];

                if (doneBuilds.Contains(build))
                    continue;

                doneBuilds.Add(build);

                //if (Directory.Exists(Path.Combine(outDir, build, "World", "Minimaps")))
                //{
                //    Console.WriteLine(build + " already extracted");
                //    continue;
                //}


                // Only do 0.X builds for now
                if (build[0] != '0')
                    continue;

                var buildOutDir = Path.Combine(outDir, build);
                if (!Directory.Exists(buildOutDir))
                    Directory.CreateDirectory(buildOutDir);
                else
                    continue;

                //// Actually lets only do 0.5.3 for now
                //if (build != "0.8.0.3734")
                //    continue;

                var subdirs = Directory.GetDirectories(directory);
                if (subdirs.Length > 1 || Path.GetFileNameWithoutExtension(subdirs[0]) != "World of Warcraft")
                    throw new Exception("Unexpected directory structure found in " + directory);

                var dataDir = Path.Combine(subdirs[0], "Data");

                // Check if WoWTest exists (old PTR)
                if (Directory.Exists(Path.Combine(subdirs[0], "WoWTest")))
                {
                    Log(build, "Old style PTR found, NYI: " + directory);
                    continue;
                }

                #region md5translate.txt/trs
                // 0.5.3-0.5.5 md5translate.txt exists on disk in Data/textures/minimap directory.
                // 0.6-0.11 md5translate.txt exists on disk and in base.MPQ in textures/minimap. Unsure how it is patched, so use on disk version for now.
                // 0.12 md5translate.txt exists in base.MPQ AND md5translate.trs patch.MPQ, we need to use the one from patch.MPQ. 
                // 1.0+ now named md5translate.trs and is in misc.MPQ and not in a Data subdir

                // TODO: 0.12
                // TODO: Use stormlib minimap extraction with proper patching
                // TODO: Use stormlib file exists check, MPQ trs first, then below checks

                if (build[0] == '1' && File.Exists(Path.Combine(dataDir, "misc.MPQ")))
                {
                    Log(build, "misc.MPQ found, attempting to extract md5translate.txt");

                    // TODO: Patches. If PTR, use the WoWTest/patch.MPQ as well.
                    ExtractFromMPQ(build, [Path.Combine(dataDir, "misc.MPQ")], [], ["textures/Minimap/md5translate.trs"], buildOutDir);

                    if (!File.Exists(Path.Combine(buildOutDir, "textures/Minimap/md5translate.trs")))
                        throw new Exception("md5translate.trs not found in extracted MPQ");
                }
                else if (File.Exists(Path.Combine(dataDir, "textures", "minimap", "md5translate.txt")))
                {
                    Log(build, "md5translate.txt was found on disk");

                    if (!Directory.Exists(Path.Combine(buildOutDir, "textures/Minimap")))
                        Directory.CreateDirectory(Path.Combine(buildOutDir, "textures/Minimap"));

                    File.Copy(Path.Combine(dataDir, "textures", "minimap", "md5translate.txt"), Path.Combine(buildOutDir, "textures/Minimap", "md5translate.trs"), true);
                }
                else if (build[0] == '0' && File.Exists(Path.Combine(dataDir, "base.MPQ")))
                {
                    Log(build, "base.MPQ found, attempting to extract md5translate.txt");
                    ExtractFromMPQ(build, [Path.Combine(dataDir, "base.MPQ")], [], ["Data/textures/Minimap/md5translate.txt"], buildOutDir);

                    if (!File.Exists(Path.Combine(buildOutDir, "Data/textures/Minimap/md5translate.txt")))
                        throw new Exception("md5translate.txt not found in extracted MPQ");

                    // Rename to expected name for processing
                    if (!Directory.Exists(Path.Combine(buildOutDir, "textures/Minimap")))
                        Directory.CreateDirectory(Path.Combine(buildOutDir, "textures/Minimap"));

                    File.Move(Path.Combine(buildOutDir, "Data/textures/Minimap/md5translate.txt"), Path.Combine(buildOutDir, "textures/Minimap/md5translate.trs"), true);

                    Directory.Delete(Path.Combine(buildOutDir, "Data"), true);
                }

                #endregion

                #region MPQs
                var targetMPQs = new List<string>();
                var patchMPQs = new List<string>();

                var options = new Options();
                options.WoWDirectory = subdirs[0];
                options.ExcludedDirectories = new HashSet<string> { "" };
                options.ExcludedExtensions = new HashSet<string> { "" };

                var dirReader = new DirectoryReader(options);
                var mpqReader = new MPQReader(options, dirReader.PatchArchives);
                mpqReader.EnumerateDataArchives(Directory.GetFiles(dataDir, "*.MPQ"), buildOutDir);

                foreach (var mpq in Directory.GetFiles(dataDir, "*.MPQ"))
                {
                    var mpqName = Path.GetFileName(mpq);

                    if (build[0] == '0')
                    {
                        if (mpqName == "texture.MPQ")
                            targetMPQs.Add(mpq);

                        if (mpqName.StartsWith("patch"))
                            patchMPQs.Add(mpq);

                    }
                }

                #endregion

                #region Rename
                if (build[0] == '0' || build[0] == '1')
                {
                    if (Directory.Exists(Path.Combine(buildOutDir, "World")))
                        Directory.Move(Path.Combine(buildOutDir, "World"), Path.Combine(buildOutDir, "WorldDupeQuestionMark"));

                    foreach (var line in File.ReadAllLines(Path.Combine(buildOutDir, "textures", "Minimap", "md5translate.trs")))
                    {
                        if (line.Substring(0, 3) == "dir")
                        {
                            // Directory
                            var targetDir = line.Remove(0, 5);

                            if (targetDir.Substring(0, 3).ToLower() == "wmo")
                                continue;

                            if (targetDir.Contains("\\"))
                            {
                                Console.ForegroundColor = ConsoleColor.Yellow;
                                Console.WriteLine("Directory " + targetDir + " has subdirectories, skipping..");
                                Console.ResetColor();
                                continue;
                            }

                            if (!Directory.Exists(Path.Combine(buildOutDir, "World", "Minimaps", targetDir)))
                            {
                                Directory.CreateDirectory(Path.Combine(buildOutDir, "World", "Minimaps", targetDir));
                            }

                            Console.WriteLine(targetDir);
                        }
                        else
                        {
                            if (line.Length == 0)
                                continue;

                            if (line.Substring(0, 4).ToLower() == "wmo\\")
                                continue;

                            var expl = line.Split('\t');
                            var targetFile = expl[0];
                            var sourceFile = expl[1];

                            Console.WriteLine(sourceFile + " => " + targetFile);

                            if(targetFile.Length == 12)
                            {
                                Console.ForegroundColor = ConsoleColor.Yellow;
                                if(build == "0.9.0.3807" || build == "0.9.1.3810")
                                {
                                    Console.WriteLine("Warning: " + targetFile + " is just a minimap tile without map. For version 0.9 this is expected for the CavernsOfTime map. Manually fixing.");
                                    Console.ResetColor();
                                    targetFile = "CavernsOfTime\\" + targetFile;

                                    if (!Directory.Exists(Path.Combine(buildOutDir, "World", "Minimaps", "CavernsOfTime")))
                                    {
                                        Directory.CreateDirectory(Path.Combine(buildOutDir, "World", "Minimaps", "CavernsOfTime"));
                                    }
                                }
                                else
                                {
                                    Console.WriteLine("Error: " + targetFile + " for build " + build + " is just a minimap tile without map. Expected for 0.9 (CavernsOfTime) but this isn't 0.9. Stop or press enter to skip.");
                                    Console.ResetColor();
                                    Console.ReadLine();
                                    continue;
                                }
                            }
                            if (targetFile.StartsWith("Kalimdor\\Tanaris\\"))
                                continue;

                            if (!File.Exists(Path.Combine(buildOutDir, "World", "Minimaps", targetFile)))
                            {
                                File.Copy(Path.Combine(buildOutDir, "textures", "Minimap", sourceFile), Path.Combine(buildOutDir, "World", "Minimaps", targetFile));
                            }
                        }
                    }
                }
                #endregion
            }
        }

        private static void Log(string build, string message)
        {
            Console.WriteLine("[" + build + "] " + message);
        }

        private static void ExtractFromMPQ(string build, List<string> inputMPQs, List<string> patchMPQs, List<string> extractFilters, string outDir)
        {
            // Because we're working off a mount that MPQ Editor can't deal with, we need to copy the input MPQ(s?) to a location it can deal with
            foreach (var inputMPQ in inputMPQs)
            {
                Log(build, "Copying " + inputMPQ + " to work directory");
                var tempMPQ = Path.Combine("work", Path.GetFileName(inputMPQ));
                if (File.Exists(tempMPQ))
                    File.Delete(tempMPQ);

                File.Copy(inputMPQ, tempMPQ);
            }

            if (inputMPQs.Count > 1)
                throw new NotImplementedException();

            var workMPQ = Path.Combine("work", Path.GetFileName(inputMPQs[0]));

            foreach(var extractFilter in extractFilters)
            {
                Log(build, "Extracting " + extractFilter + " from " + workMPQ + " into " + outDir);

                Console.WriteLine("/extract \"" + workMPQ + "\" \"" + extractFilter + "\" \"" + outDir + "\" /fp");

                var mpqEditor = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "MPQEditor.exe",
                        Arguments = "/extract \"" + workMPQ + "\" \"" + extractFilter + "\" \"" + outDir + "\" /fp",
                        UseShellExecute = false,
                    }
                };

                mpqEditor.Start();
                mpqEditor.WaitForExit();
            }
        }
    }
}
