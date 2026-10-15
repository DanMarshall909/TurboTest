namespace TurboTest.Tests;

// These tests deliberately change process-wide owners and failed-cleanup state.
[CollectionDefinition("Static lifecycle")]
public class StaticLifecycleCollection;
