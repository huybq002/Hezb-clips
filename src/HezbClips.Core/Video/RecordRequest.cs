using Rectangle = System.Drawing.Rectangle;
using HezbClips.Core.Audio;
using HezbClips.Core.Hardware;
using HezbClips.Core.Settings;

namespace HezbClips.Core.Video;

public record RecordRequest(Rectangle Region, int Fps, AudioSources Audio, ExportQuality Quality, RecordMode Mode, bool SkipUpscale = false);

public record RecordResult(string FinalPath, Guid? PendingUpscaleJob, IReadOnlyList<string> Warnings);
