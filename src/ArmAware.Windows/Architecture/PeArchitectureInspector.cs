using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using ArmAware.Core.Interfaces;
using ArmAware.Core.Models;
namespace ArmAware.Windows.Architecture;

public sealed class PeArchitectureInspector : IArchitectureInspector
{
    public Task<ArchitectureInspectionResult> InspectAsync(string executablePath, CancellationToken cancellationToken)
        => Task.Run(() => Inspect(executablePath, cancellationToken), cancellationToken);

    private static ArchitectureInspectionResult Inspect(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            // Read headers/selected directories only. Never load or execute the inspected image.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var pe = new PEReader(stream);
            var headers = pe.PEHeaders;
            if (headers.PEHeader is not { } optional) throw new BadImageFormatException("Not an executable PE image.");
            var machine = (ushort)headers.CoffHeader.Machine;
            var architecture = machine switch
            {
                0x14c => BinaryArchitecture.X86,
                0x8664 => BinaryArchitecture.X64,
                0xaa64 => BinaryArchitecture.Arm64,
                0xa641 => BinaryArchitecture.Arm64EC,
                0xa64e => BinaryArchitecture.Arm64X,
                _ => BinaryArchitecture.Unknown
            };
            var evidence = $"COFF Machine=0x{machine:X4}.";
            if (headers.CorHeader is { } cor)
            {
                evidence += $" CLR flags={cor.Flags}.";
                if (machine == 0x14c && cor.Flags.HasFlag(CorFlags.ILOnly) && !cor.Flags.HasFlag(CorFlags.Requires32Bit))
                    return new(path, BinaryArchitecture.AnyCpu, Confidence.High, "PE + CLR header", evidence,
                        "AnyCPU runtime selection depends on the launcher/runtime; Prefer32Bit does not establish installed native architecture.");
                if (!cor.Flags.HasFlag(CorFlags.ILOnly))
                    return new(path, BinaryArchitecture.Unknown, Confidence.Low, "PE + CLR header", evidence,
                        "Mixed-mode managed image; execution architecture requires further analysis.");
            }
            // Final linked hybrid images can use AMD64 or ARM64 in COFF, unlike ARM64EC objects.
            if (optional.Magic == PEMagic.PE32Plus && optional.LoadConfigTableDirectory is { RelativeVirtualAddress: > 0, Size: >= 208 } directory)
            {
                long offset = RvaToOffset(headers, directory.RelativeVirtualAddress, 208);
                if (offset < 0 || offset + 208 > stream.Length) throw new BadImageFormatException("Invalid load configuration bounds.");
                stream.Position = offset;
                Span<byte> loadConfig = stackalloc byte[208];
                stream.ReadExactly(loadConfig);
                if (BinaryPrimitives.ReadUInt32LittleEndian(loadConfig) >= 208 && BinaryPrimitives.ReadUInt64LittleEndian(loadConfig[200..]) != 0)
                {
                    evidence += " Nonzero CHPEMetadataPointer in IMAGE_LOAD_CONFIG_DIRECTORY64.";
                    architecture = machine switch { 0xaa64 => BinaryArchitecture.Arm64X, 0x8664 => BinaryArchitecture.Arm64EC, _ => architecture };
                }
            }
            ct.ThrowIfCancellationRequested();
            return new(path, architecture, architecture == BinaryArchitecture.Unknown ? Confidence.None : Confidence.High,
                "PE COFF / CLR / load configuration", evidence, "Describes this image only; dependencies and live processes are not inspected.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException or ArgumentException or NotSupportedException)
        {
            return new(path, BinaryArchitecture.Unknown, Confidence.None, "PE inspection", "No reliable architecture evidence.", ex.Message);
        }
    }

    private static long RvaToOffset(PEHeaders headers, int rva, int size)
    {
        foreach (var section in headers.SectionHeaders)
        {
            long delta = (long)rva - section.VirtualAddress;
            if (delta >= 0 && delta + size <= section.SizeOfRawData) return section.PointerToRawData + delta;
        }
        return -1;
    }
}
