// Encodes a folder of numbered JPEG frames into an H.264 MP4 using Windows' built-in media stack (no ffmpeg).
// Usage: encoder <framesDir> <output.mp4> [fps]
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

var framesDir = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
var fps = args.Length > 2 ? int.Parse(args[2]) : 30;
var frames = Directory.GetFiles(framesDir, "frame_*.jpg").Order(StringComparer.Ordinal).ToArray();
var composition = new MediaComposition();
var frameTicks = TimeSpan.TicksPerSecond / fps;
for (var i = 0; i < frames.Length; i++)
{
    var file = await StorageFile.GetFileFromPathAsync(frames[i]);
    composition.Clips.Add(await MediaClip.CreateFromImageFileAsync(file, TimeSpan.FromTicks(frameTicks)));
    if (i % 120 == 0) Console.WriteLine($"added {i}/{frames.Length}");
}
var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
profile.Video!.Width = 1920; profile.Video.Height = 1080;
profile.Video.FrameRate.Numerator = (uint)fps; profile.Video.FrameRate.Denominator = 1;
profile.Video.Bitrate = 16_000_000;
profile.Audio = null;
var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(output)!);
var target = await folder.CreateFileAsync(Path.GetFileName(output), CreationCollisionOption.ReplaceExisting);
var render = composition.RenderToFileAsync(target, MediaTrimmingPreference.Precise, profile);
render.Progress = (_, p) => { if ((int)p % 10 == 0) Console.WriteLine($"encoding {p:F0}%"); };
var result = await render;
Console.WriteLine(result == TranscodeFailureReason.None ? $"wrote {output} ({new FileInfo(output).Length / 1024 / 1024} MB)" : $"failed: {result}");
return result == TranscodeFailureReason.None ? 0 : 1;
