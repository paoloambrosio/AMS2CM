﻿using System.Collections.Immutable;
using Core.Games;
using Core.Packages.Installation.Installers;
using Core.Utils;
using Microsoft.Extensions.FileSystemGlobbing;

namespace Core.Mods.Installation.Installers;

/**
 * Wrapper over mod installer that generates configuration for game or bootfiles.
 */
public class ModInstaller : BaseModInstaller
{
    public new interface IConfig : BaseModInstaller.IConfig
    {
        IEnumerable<string> ExcludedFromConfig
        {
            get;
        }

        bool GenerateModDetails
        {
            get;
        }
    }

    private readonly Matcher filesToConfigureMatcher;
    private readonly bool generateModDetails;

    private IReadOnlyCollection<string> bootfilesDependency = Array.Empty<string>();
    private readonly string bootfilesPackageName;

    internal ModInstaller(IInstaller inner, string bootfilesPackageName, IGame game, ITempDir tempDir, IConfig config) :
        base(inner, game, tempDir, config)
    {
        this.bootfilesPackageName = bootfilesPackageName;
        filesToConfigureMatcher = Matchers.ExcludingPatterns(config.ExcludedFromConfig);
        generateModDetails = config.GenerateModDetails;
    }

    public override IReadOnlyCollection<string> PackageDependencies =>
        Inner.PackageDependencies.Concat(bootfilesDependency).ToImmutableList();

    protected override void Install(Action innerInstall)
    {
        innerInstall();

        GenerateModConfig();
    }

    private void GenerateModConfig()
    {
        var gameSupportedMod = FileEntriesToConfigure()
            .Any(p => p.StartsWith(PostProcessor.GameSupportedModDirectory));
        var modConfig = gameSupportedMod
            ? ConfigEntries.Empty
            : new ConfigEntries(CrdFileEntries(), TrdFileEntries(), FindDrivelineRecords());
        WriteModConfigFiles(modConfig);
    }

    private void WriteModConfigFiles(ConfigEntries modConfig)
    {
        if (modConfig.None())
            return;

        var normalisedName = string.Concat(
            Path.GetFileNameWithoutExtension(Inner.PackageName)
                .Where(char.IsAsciiLetterOrDigit));
        var hexFsHash = (PackageFsHash ?? 0).ToString("x");
        var modConfigDirPath = new RootedPath(
            Game.InstallationDirectory,
            Path.Combine(PostProcessor.GameSupportedModDirectory, $"{normalisedName}_{hexFsHash}"));

        // TODO this can fail
        Directory.CreateDirectory(modConfigDirPath.Full);
        AddToInstalledFiles(PostProcessor.AppendCrdFileEntries(modConfigDirPath, modConfig.CrdFileEntries));
        AddToInstalledFiles(PostProcessor.AppendTrdFileEntries(modConfigDirPath, modConfig.TrdFileEntries));
        AddToInstalledFiles(PostProcessor.AppendDrivelineRecords(modConfigDirPath, modConfig.DrivelineRecords));
        if (generateModDetails && !modConfig.TrdFileEntries.Any())
        {
            AddToInstalledFiles(PostProcessor.GenerateModDetails(modConfigDirPath, Inner));
        } else
        {
            bootfilesDependency = new[] { bootfilesPackageName };
        }
    }

    private List<string> CrdFileEntries() =>
        FileEntriesToConfigure()
            .Where(p => p.EndsWith(".crd"))
            .ToList();

    private List<string> TrdFileEntries() =>
        FileEntriesToConfigure()
            .Where(p => p.EndsWith(".trd"))
            .Select(fp => $"{Path.GetDirectoryName(fp)}{Path.DirectorySeparatorChar}@{Path.GetFileName(fp)}")
            .ToList();

    private IEnumerable<string> FileEntriesToConfigure() =>
        Inner.InstalledFiles
            .Select(rp => rp.Relative)
            .Where(p => filesToConfigureMatcher.Match(p).HasMatches);

    private List<string> FindDrivelineRecords()
    {
        var recordBlocks = new List<string>();
        if (!StagingDir.Exists)
        {
            return recordBlocks;
        }

        foreach (var configFile in StagingDir.EnumerateFiles())
        {
            var recordIndent = -1;
            var recordLines = new List<string>();
            foreach (var line in File.ReadAllLines(configFile.FullName))
            {
                // Read each line until we find one with RECORD
                if (recordIndent < 0)
                {
                    recordIndent = line.IndexOf("RECORD", StringComparison.InvariantCulture);
                }
                if (recordIndent < 0)
                {
                    continue;
                }

                // Once it finds a blank line, create a record block and start over
                if (string.IsNullOrWhiteSpace(line))
                {
                    recordBlocks.Add(string.Join(Environment.NewLine, recordLines));
                    recordIndent = -1;
                    recordLines.Clear();
                    continue;
                }

                // Otherwise add the line to the current record lines
                var lineNoIndent = line.Substring(recordIndent).TrimEnd();
                recordLines.Add(lineNoIndent);
            }

            // Create a record block also if the file finshed on a record line
            if (recordIndent >= 0)
            {
                recordBlocks.Add(string.Join(Environment.NewLine, recordLines));
            }
        }

        return recordBlocks;
    }

}
