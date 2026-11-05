// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Crateris.Core.Tests;

/// <summary>
/// The test topics (tests/topics.json, tools/run_tests.py) stay whole:
/// every test class is in one known topic, every topic has tests, and
/// every source file and UQM recording is covered by a topic, so running
/// only the topics a change touches can't miss one.
/// </summary>
[Trait("Topic", "world")]
public class TopicTests
{
    private static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "tests", "topics.json")))
                return dir.FullName;
        }
        throw new InvalidOperationException("tests/topics.json isn't above the test's folder");
    }

    private static readonly Lazy<JsonElement> Topics = new(() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(Root(), "tests", "topics.json"))).RootElement);

    /// <summary>A glob as tools/run_tests.py reads it: ** any folders, * anything in one name.</summary>
    public static Regex Glob(string glob)
    {
        var pattern = Regex.Escape(glob).Replace(@"\*\*/", "(.*/)?").Replace(@"\*\*", ".*").Replace(@"\*", "[^/]*")
            .Replace(@"\?", "[^/]");
        return new Regex("^" + pattern + "$");
    }

    private static IEnumerable<Type> TestClasses() => typeof(TopicTests).Assembly.GetTypes()
        .Where(t => t.IsClass && !t.IsAbstract && t.GetMethods().Any(m =>
            m.GetCustomAttributes().Any(a => a is FactAttribute)));

    // A trait's name and value are in the attribute's data (TraitAttribute doesn't keep them).
    private static string? TopicOf(Type t) => t.GetCustomAttributesData()
        .Where(d => d.AttributeType == typeof(TraitAttribute) && (string?)d.ConstructorArguments[0].Value == "Topic")
        .Select(d => (string?)d.ConstructorArguments[1].Value)
        .SingleOrDefault();

    [Fact]
    public void EveryTestClassIsInOneKnownTopic()
    {
        var known = Topics.Value.GetProperty("topics").EnumerateObject().Select(t => t.Name).ToHashSet();
        var classes = TestClasses().ToList();
        Assert.True(classes.Count > 40, $"only {classes.Count} test classes");
        foreach (var t in classes)
        {
            string? topic = TopicOf(t);
            Assert.True(topic != null && known.Contains(topic), $"{t.Name} has no topic in tests/topics.json ({topic})");
        }
        var used = classes.Select(TopicOf).ToHashSet();
        Assert.All(known, k => Assert.True(used.Contains(k), $"topic {k} has no tests"));
    }

    [Fact]
    public void EverySourceFileAndRecordingIsInATopic()
    {
        string root = Root();
        var globs = Topics.Value.GetProperty("all").EnumerateArray()
            .Concat(Topics.Value.GetProperty("topics").EnumerateObject().SelectMany(t => t.Value.GetProperty("paths").EnumerateArray()))
            .Select(g => Glob(g.GetString()!)).ToList();
        var files = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "tests", "golden"), "*.json"))
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .Where(f => !f.Contains("/bin/") && !f.Contains("/obj/"))
            .ToList();
        Assert.True(files.Count > 100, $"only {files.Count} files");
        var uncovered = files.Where(f => !globs.Any(g => g.IsMatch(f))).ToList();
        Assert.True(uncovered.Count == 0, "not in any topic: " + string.Join(", ", uncovered));
    }

    [Fact]
    public void GlobsReadAsTheRunnerReadsThem()
    {
        Assert.Matches(Glob("src/Crateris.Core/Uqm/**"), "src/Crateris.Core/Uqm/UqmMath.cs");
        Assert.Matches(Glob("src/**/x.cs"), "src/x.cs");
        Assert.Matches(Glob("tests/golden/ip-*.json"), "tests/golden/ip-flight.json");
        Assert.DoesNotMatch(Glob("tests/golden/ip-*.json"), "tests/golden/sub/ip-flight.json");
        Assert.DoesNotMatch(Glob("src/Crateris.Core/Uqm/*"), "src/Crateris.Core/Uqm/a/b.cs");
    }
}
