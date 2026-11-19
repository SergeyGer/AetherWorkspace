using Aether.Contracts.Instructions;
using Xunit;

namespace Aether.Contracts.Tests;

/// <summary>v1.5.1 local instruction library: atomic CRUD, starter templates, defensive load.</summary>
public sealed class InstructionTemplateStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "aether-instructions-" + Guid.NewGuid().ToString("n"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    private InstructionTemplateStore NewStore() =>
        new(Path.Combine(_directory, "instructions.json"));

    [Fact]
    public void MissingFileReturnsThreeSyntheticStarters()
    {
        var store = NewStore();
        var result = store.Load();

        Assert.Null(result.Error);
        Assert.Equal(3, result.Templates.Count);
        Assert.All(result.Templates, template => Assert.False(string.IsNullOrWhiteSpace(template.Instruction)));
        Assert.Contains(result.Templates, template => template.Title.Contains("Northstar", StringComparison.Ordinal));
    }

    [Fact]
    public void SaveLoadRenameDuplicateDeleteRoundTrip()
    {
        var store = NewStore();
        var first = InstructionTemplateStore.NewTemplate("Vega analytics", "Open Vega analytics and summarize privately.");
        var save = store.Save([first]);
        Assert.True(save.Success, save.Error);

        var loaded = store.Load();
        Assert.Null(loaded.Error);
        Assert.Single(loaded.Templates);
        Assert.Equal("Vega analytics", loaded.Templates[0].Title);

        var renamed = InstructionTemplateStore.WithUpdatedText(loaded.Templates[0], "Vega analytics (renamed)", loaded.Templates[0].Instruction);
        Assert.True(store.Save([renamed]).Success);

        var duplicated = InstructionTemplateStore.Duplicate(renamed);
        Assert.True(store.Save([renamed, duplicated]).Success);
        Assert.Equal(2, store.Load().Templates.Count);

        var deleted = store.Load().Templates.Where(template => template.Id != renamed.Id).ToArray();
        Assert.True(store.Save(deleted).Success);
        Assert.Single(store.Load().Templates);
    }

    [Fact]
    public void InvalidTemplatesAreRejected()
    {
        Assert.False(InstructionTemplateStore.TryValidate(" ", "instruction", out _));
        Assert.False(InstructionTemplateStore.TryValidate("title", " ", out _));
        Assert.False(InstructionTemplateStore.TryValidate(new string('x', 121), "instruction", out _));
        Assert.False(InstructionTemplateStore.TryValidate("title", new string('x', 4001), out _));
        Assert.Throws<ArgumentException>(() => InstructionTemplateStore.NewTemplate("", "instruction"));
    }

    [Fact]
    public void DuplicateTitlesAreRejected()
    {
        var store = NewStore();
        var a = InstructionTemplateStore.NewTemplate("Same title", "Instruction A");
        var b = InstructionTemplateStore.NewTemplate("same title", "Instruction B");

        var result = store.Save([a, b]);

        Assert.False(result.Success);
        Assert.Contains("unique", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CorruptFileIsQuarantinedAndNeverExecuted()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "instructions.json");
        File.WriteAllText(path, "{not valid json");

        var store = new InstructionTemplateStore(path);
        var result = store.Load();

        Assert.Empty(result.Templates);
        Assert.NotNull(result.Error);
        Assert.NotNull(result.QuarantinedPath);
        Assert.False(File.Exists(path));
        Assert.True(File.Exists(result.QuarantinedPath));
    }

    [Fact]
    public void OversizedFileIsQuarantined()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "instructions.json");
        File.WriteAllText(path, new string('x', InstructionTemplateStore.MaxFileBytes + 1));

        var result = new InstructionTemplateStore(path).Load();

        Assert.Empty(result.Templates);
        Assert.Contains("512", result.Error, StringComparison.Ordinal);
        Assert.NotNull(result.QuarantinedPath);
    }
}
