using CUE4Parse.UE4.Assets.Exports;

namespace Viewer;

public record PackageContents(
    IReadOnlyList<UObject> Exports,
    IReadOnlyList<string> Imports);
