using Xunit;

namespace SimpleWall.Tests
{
    /// <summary>
    /// libvlc's native init (Core.Initialize + libvlc_new) is not safe to run CONCURRENTLY on the
    /// build VM (ARM64 emulating x64): two test collections each calling it in parallel
    /// access-violate inside libvlc_new. xunit parallelises across collections by default, so
    /// every class that constructs a LibVLC / MediaPlayer / ThumbnailCache shares THIS one
    /// collection -- xunit runs a single collection's tests sequentially, and
    /// DisableParallelization keeps it from overlapping any other collection too.
    ///
    /// This is a TEST-INFRA constraint, not a product one: production serialises thumbnail
    /// extraction (ThumbnailCache._oneAtATime) and builds the wall engine's LibVLC exactly once.
    /// The flakiness was latent -- the two libvlc classes could always collide; adding unrelated
    /// tests merely changed xunit's scheduling enough to make it happen every run.
    /// </summary>
    [CollectionDefinition("LibVlc", DisableParallelization = true)]
    public class LibVlcCollection { }
}
