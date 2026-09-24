namespace Stanley.App.Updates;

/// <summary>Which release track File &gt; Options &gt; Updates checks. <see cref="Nightly"/> follows every
/// change to `develop` (see docs/automatic-builds.md) and is meant for testers, not the default audience.</summary>
public enum AppUpdateChannel
{
    Stable,
    Nightly
}
