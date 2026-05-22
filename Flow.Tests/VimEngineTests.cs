using System.Collections.Generic;
using System.Windows.Input;

namespace Flow.Tests;

public sealed class VimEngineTests
{
    [Fact]
    public void HandleKey_WhenSingleKeyCommandRegistered_ExecutesCommand()
    {
        var engine = new VimEngine();
        var executed = new List<string>();

        engine.Register("h", _ => executed.Add("h"));

        bool handled = engine.HandleKey(Key.H, shift: false, context: null!);

        Assert.True(handled);
        Assert.Equal(["h"], executed);
    }

    [Fact]
    public void HandleKey_WhenSequenceIsPrefix_WaitsForRemainingKeys()
    {
        var engine = new VimEngine();
        var executed = new List<string>();

        engine.Register("gg", _ => executed.Add("gg"));

        bool firstHandled = engine.HandleKey(Key.G, shift: false, context: null!);
        bool secondHandled = engine.HandleKey(Key.G, shift: false, context: null!);

        Assert.True(firstHandled);
        Assert.True(secondHandled);
        Assert.Equal(["gg"], executed);
    }

    [Fact]
    public void HandleKey_WhenBufferedSequenceDoesNotMatch_FallsBackToSingleKeyCommand()
    {
        var engine = new VimEngine();
        var executed = new List<string>();

        engine.Register("gg", _ => executed.Add("gg"));
        engine.Register("x", _ => executed.Add("x"));

        engine.HandleKey(Key.G, shift: false, context: null!);
        bool handled = engine.HandleKey(Key.X, shift: false, context: null!);

        Assert.True(handled);
        Assert.Equal(["x"], executed);
    }

    [Fact]
    public void HandleKey_WhenShiftedKeyCommandRegistered_UsesShiftAwareNotation()
    {
        var engine = new VimEngine();
        var executed = new List<string>();

        engine.Register("G", _ => executed.Add("G"));
        engine.Register("$", _ => executed.Add("$"));

        bool handledLast = engine.HandleKey(Key.G, shift: true, context: null!);
        bool handledEnd = engine.HandleKey(Key.D4, shift: true, context: null!);

        Assert.True(handledLast);
        Assert.True(handledEnd);
        Assert.Equal(["G", "$"], executed);
    }

    [Fact]
    public void HandleKey_WhenVisualModeHasOverride_PrefersModeSpecificCommand()
    {
        var engine = new VimEngine();
        var executed = new List<string>();

        engine.Register("d", _ => executed.Add("normal"));
        engine.Register(VimMode.Visual, "d", _ => executed.Add("visual"));
        engine.SetMode(VimMode.Visual);

        bool handled = engine.HandleKey(Key.D, shift: false, context: null!);

        Assert.True(handled);
        Assert.Equal(["visual"], executed);
    }

    [Fact]
    public void HandleKey_WhenModeSpecificCommandMissing_FallsBackToNormalModeRegistry()
    {
        var engine = new VimEngine();
        var executed = new List<string>();

        engine.Register("h", _ => executed.Add("normal"));
        engine.SetMode(VimMode.VisualLine);

        bool handled = engine.HandleKey(Key.H, shift: false, context: null!);

        Assert.True(handled);
        Assert.Equal(["normal"], executed);
    }

    [Fact]
    public void TryExitToNormalMode_WhenBufferExists_ClearsPendingSequence()
    {
        var engine = new VimEngine();
        var executed = new List<string>();

        engine.Register("gg", _ => executed.Add("gg"));

        engine.HandleKey(Key.G, shift: false, context: null!);

        bool exited = engine.TryExitToNormalMode();
        bool firstHandledAfterClear = engine.HandleKey(Key.G, shift: false, context: null!);
        bool secondHandledAfterClear = engine.HandleKey(Key.G, shift: false, context: null!);

        Assert.False(exited);
        Assert.True(firstHandledAfterClear);
        Assert.True(secondHandledAfterClear);
        Assert.Equal(["gg"], executed);
    }

    [Fact]
    public void HandleKey_WhenKeyIsNotMapped_ReturnsFalse()
    {
        var engine = new VimEngine();

        bool handled = engine.HandleKey(Key.Q, shift: false, context: null!);

        Assert.False(handled);
    }
}
