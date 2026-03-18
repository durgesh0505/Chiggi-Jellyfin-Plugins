using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;

namespace Trakt.Helpers;

/// <summary>
/// Equality comparer that uses the Jellyfin item Id to deduplicate BaseItem instances.
/// </summary>
internal sealed class ItemIdEqualityComparer<T> : IEqualityComparer<T>
    where T : BaseItem
{
    /// <summary>Gets the singleton instance.</summary>
    public static readonly ItemIdEqualityComparer<T> Instance = new();

    private ItemIdEqualityComparer()
    {
    }

    /// <inheritdoc />
    public bool Equals(T? x, T? y)
    {
        if (x is null && y is null)
        {
            return true;
        }

        if (x is null || y is null)
        {
            return false;
        }

        return x.Id == y.Id;
    }

    /// <inheritdoc />
    public int GetHashCode([DisallowNull] T obj) => obj.Id.GetHashCode();
}

/// <summary>
/// Class that contains all the items to be reported to trakt.tv and supporting properties.
/// </summary>
internal sealed class UserDataPackage
{
    public UserDataPackage()
    {
        // Fix #226: Use HashSet by Id to prevent duplicate entries when Jellyfin fires
        // multiple UserDataSaved events for the same item in a single save operation.
        SeenMovies = new HashSet<Movie>(ItemIdEqualityComparer<Movie>.Instance);
        UnSeenMovies = new HashSet<Movie>(ItemIdEqualityComparer<Movie>.Instance);
        SeenEpisodes = new HashSet<Episode>(ItemIdEqualityComparer<Episode>.Instance);
        UnSeenEpisodes = new HashSet<Episode>(ItemIdEqualityComparer<Episode>.Instance);
    }

    public Guid? CurrentSeriesId { get; set; }

    public ICollection<Movie> SeenMovies { get; set; }

    public ICollection<Movie> UnSeenMovies { get; set; }

    public ICollection<Episode> SeenEpisodes { get; set; }

    public ICollection<Episode> UnSeenEpisodes { get; set; }
}
