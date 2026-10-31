using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ArmAware.Core.Models;
using ArmAware.Windows.Architecture;
namespace ArmAware.Tests;

public sealed class ArchitectureTests
{
    [Theory]
    [InlineData(0x14c, BinaryArchitecture.X86)]
    [InlineData(0x8664, BinaryArchitecture.X64)]
    [InlineData(0xaa64, BinaryArchitecture.Arm64)]
    [InlineData(0xa641, BinaryArchitecture.Arm64EC)]
    [InlineData(0xa64e, BinaryArchitecture.Arm64X)]
    [InlineData(0x1c4, BinaryArchitecture.Unknown)]
    public async Task CoffMachineIsInspected(int machine, BinaryArchitecture expected)
    {
        var path = Fixture((ushort)machine);
        try { Assert.Equal(expected, (await new PeArchitectureInspector().InspectAsync(path, default)).Architecture); }
        finally { File.Delete(path); }
    }
    [Theory]
    [InlineData(0xaa64, BinaryArchitecture.Arm64X)]
    [InlineData(0x8664, BinaryArchitecture.Arm64EC)]
    public async Task HybridMetadataOverridesLegacyMachine(int machine, BinaryArchitecture expected)
    {
        var path = Fixture((ushort)machine, hybrid: true);
        try { var result = await new PeArchitectureInspector().InspectAsync(path, default); Assert.Equal(expected, result.Architecture); Assert.Contains("CHPE", result.Evidence); }
        finally { File.Delete(path); }
    }
    [Fact]
    public async Task ManagedAnyCpuIsNotX86()
    {
        // Test assembly is deliberately AnyCPU and is never loaded for inspection.
        var result = await new PeArchitectureInspector().InspectAsync(typeof(ArchitectureTests).Assembly.Location, default);
        Assert.Equal(BinaryArchitecture.AnyCpu, result.Architecture);
    }
    [Fact]
    public async Task MissingFileIsUnknown() => Assert.Equal(BinaryArchitecture.Unknown,
        (await new PeArchitectureInspector().InspectAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe"), default)).Architecture);
    [Theory]
    [InlineData("")]
    [InlineData("not a PE")]
    [InlineData("MZbroken header")]
    public async Task InvalidFilesAreUnknown(string content)
    {
        var path = Path.GetTempFileName();
        try { await File.WriteAllTextAsync(path, content); Assert.Equal(BinaryArchitecture.Unknown, (await new PeArchitectureInspector().InspectAsync(path, default)).Architecture); }
        finally { File.Delete(path); }
    }
    [Fact]
    public async Task CancellationIsObserved()
    {
        using var ct = new CancellationTokenSource(); ct.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new PeArchitectureInspector().InspectAsync("unused", ct.Token));
    }
    private static string Fixture(ushort machine, bool hybrid = false)
    {
        // Controlled header-only fixture; no executable instructions and never executed.
        var path = Path.GetTempFileName();
        var data = new byte[1024];
        using var stream = new MemoryStream(data);
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0x5a4d); stream.Position = 0x3c; writer.Write(0x80);
        stream.Position = 0x80; writer.Write(0x4550); writer.Write(machine); writer.Write((ushort)1);
        stream.Position = 0x94; writer.Write((ushort)240); writer.Write((ushort)0x22);
        stream.Position = 0x98; writer.Write((ushort)0x20b);
        stream.Position = 0x98 + 32; writer.Write(0x1000); writer.Write(0x200);
        stream.Position = 0x98 + 56; writer.Write(0x2000); writer.Write(0x200);
        stream.Position = 0x98 + 108; writer.Write(16);
        if (hybrid) { stream.Position = 0x98 + 112 + 10 * 8; writer.Write(0x1000); writer.Write(208); }
        stream.Position = 0x188; writer.Write(new byte[] { 46, 114, 100, 97, 116, 97, 0, 0 });
        writer.Write(0x200); writer.Write(0x1000); writer.Write(0x200); writer.Write(0x200);
        if (hybrid) { stream.Position = 0x200; writer.Write(208); stream.Position = 0x200 + 200; writer.Write(0x180001100UL); }
        File.WriteAllBytes(path, data); return path;
    }
}
