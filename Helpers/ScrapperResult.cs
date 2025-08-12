using Playnite.SDK.Models;
using System.Collections.Generic;

#nullable enable

namespace F95ZoneMetadataProvider
{
    /// <summary>
    /// Represents the result of a scrapping operation, containing metadata and related resources.
    /// </summary>
    public class ScrapperResult
    {
        /// <summary>
        /// Gets or sets the unique identifier of the scraped item.
        /// </summary>
        public string? Id { get; set; }

        /// <summary>
        /// Gets or sets the name of the scraped item.
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// Gets or sets the collection of labels associated with the item.
        /// </summary>
        public List<string>? Labels { get; set; }

        /// <summary>
        /// Gets or sets the version string of the scraped item.
        /// </summary>
        public string? Version { get; set; }

        /// <summary>
        /// Gets or sets the name of the developer or publisher.
        /// </summary>
        public string? Developer { get; set; }

        /// <summary>
        /// Gets or sets the detailed description of the scraped item.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Gets or sets the list of tags categorizing the item.
        /// </summary>
        public List<string>? Tags { get; set; }

        /// <summary>
        /// Gets or sets the average user rating, expressed as a numeric value.
        /// </summary>
        public double Rating { get; set; }

        /// <summary>
        /// Gets or sets the list of image URLs or file paths associated with the item.
        /// </summary>
        public List<string>? Images { get; set; }

        /// <summary>
        /// Gets or sets the collection of related links (e.g., homepage, repository).
        /// </summary>
        public List<Link>? Links { get; set; }
    }

    /// <summary>
    /// Represents a single search result item returned by the scraper.
    /// </summary>
    public class ScrapperSearchResult
    {
        /// <summary>
        /// Gets or sets the URL link to the scraped search result.
        /// </summary>
        /// <value>The full URL as a string.</value>
        public string? Link { get; set; }

        /// <summary>
        /// Gets or sets the display name or title of the search result.
        /// </summary>
        /// <value>The name or title as a string.</value>
        public string? Name { get; set; }

        /// <summary>
        /// Gets or sets a short description or snippet for the search result.
        /// </summary>
        /// <value>A brief description as a string.</value>
        public string? Description { get; set; }
    }
}