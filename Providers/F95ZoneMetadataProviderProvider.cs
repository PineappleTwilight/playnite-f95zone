using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;

#nullable enable

namespace F95ZoneMetadataProvider
{
    public class F95ZoneMetadataProviderProvider : OnDemandMetadataProvider
    {
        private const string IconUrl = "https://static.f95zone.to/assets/favicon-32x32.png";

        public override List<MetadataField> AvailableFields => F95ZoneMetadataProvider.Fields;

        private readonly MetadataRequestOptions options;
        private readonly F95ZoneMetadataProvider plugin;
        private Game Game => options.GameData;
        private bool IsBackgroundDownload => options.IsBackgroundDownload;

        // public override List<MetadataField> AvailableFields => throw new NotImplementedException();

        public F95ZoneMetadataProviderProvider(MetadataRequestOptions options, F95ZoneMetadataProvider plugin)
        {
            this.options = options;
            this.plugin = plugin;
        }

        private ScraperResult? _result;
        private Helpers.ApiGame? _apiGame;
        private bool _didRun;

        /// <summary>
        /// Extracts the thread ID from the specified link if it starts with the scraper's default base URL.
        /// </summary>
        /// <param name="link">The URL to extract the thread ID from.</param>
        /// <returns>
        /// The extracted thread ID with any trailing slash removed and file extension omitted,
        /// or <c>null</c> if the link does not start with the default base URL.
        /// </returns>
        public static string? GetIdFromLink(string link)
        {
            if (!link.StartsWith(Scraper.DefaultBaseUrl, StringComparison.OrdinalIgnoreCase))
                return null;

            var threadId = link.Substring(Scraper.DefaultBaseUrl.Length);

            if (threadId.EndsWith("/"))
                threadId = threadId.Substring(0, threadId.Length - 1);

            var dotIndex = threadId.IndexOf('.');
            return dotIndex == -1
                ? threadId
                : threadId.Substring(dotIndex + 1);
        }

        /// <summary>
        /// Attempts to extract an identifier from the provided <see cref="Game"/> object.
        /// It first tries to parse the identifier from the <see cref="Game.Name"/> property
        /// using <see cref="GetIdFromLink(string)"/>, then checks if the name starts with "F95-".
        /// If those attempts fail, it looks for a link named "F95zone" in <see cref="Game.Links"/>
        /// and attempts to extract the identifier from its URL.
        /// </summary>
        /// <param name="game">The <see cref="Game"/> instance to extract an identifier from.</param>
        /// <returns>
        /// The extracted identifier as a string if found; otherwise, <c>null</c>.
        /// </returns>
        public static string? GetIdFromGame(Game game)
        {
            if (game.Name is not null)
            {
                {
                    var threadId = GetIdFromLink(game.Name);
                    if (threadId is not null) return threadId;
                }

                if (game.Name.StartsWith("F95-", StringComparison.OrdinalIgnoreCase))
                {
                    var threadId = game.Name.Substring(4);
                    return threadId;
                }

                if (int.TryParse(game.Name, out _))
                {
                    return game.Name;
                }
            }

            var f95Link = game.Links?.FirstOrDefault(link => link.Name.Equals("F95zone", StringComparison.OrdinalIgnoreCase));
            if (f95Link is not null && !string.IsNullOrWhiteSpace(f95Link.Url))
            {
                return GetIdFromLink(f95Link.Url);
            }

            return null;
        }

        /// <summary>
        /// Retrieves the result of scraping metadata for the specified game.
        /// </summary>
        /// <remarks>This method attempts to retrieve metadata for the game by either using a
        /// pre-determined ID or performing  a search based on the game's name. If the game ID cannot be determined and
        /// the operation is running in  the background, the first search result is selected automatically. Otherwise,
        /// the user is prompted to  choose a result from a search dialog.   The method ensures that the scraping
        /// operation is only performed once per instance. If the operation  has already been executed, the cached
        /// result is returned.  Exceptions may be thrown if critical conditions are not met, such as the inability to
        /// determine a valid  game ID from the search results.</remarks>
        /// <param name="args">The arguments required for metadata retrieval, including a cancellation token.</param>
        /// <returns>A <see cref="ScraperResult"/> containing the scraped metadata for the game, or <see langword="null"/>  if
        /// the operation fails or no metadata is found.</returns>
        /// <exception cref="NotImplementedException">Thrown if a valid game ID cannot be determined from the search results.</exception>
        private ScraperResult? GetResult(GetMetadataFieldArgs args)
        {
            if (_didRun) return _result;

            var scrapper = new Scraper(F95ZoneMetadataProvider.Logger, F95ZoneMetadataProvider.SharedClient, F95ZoneMetadataProvider.SharedHandler);

            var id = GetIdFromGame(Game);
            if (id is null)
            {
                if (string.IsNullOrWhiteSpace(Game.Name))
                {
                    F95ZoneMetadataProvider.Logger.Error("Unable to get Id from Game and Name is null or whitespace!");
                    _didRun = true;
                    return null;
                }

                if (IsBackgroundDownload)
                {
                    // background download so we just choose the first item

                    var apiSearchTask = F95ZoneMetadataProvider.ApiService.GetGames(Game.Name);
                    apiSearchTask.Wait(args.CancelToken);
                    var apiResults = apiSearchTask.Result;

                    if (apiResults != null && apiResults.Any())
                    {
                        _apiGame = apiResults.First();
                        id = _apiGame.ThreadId.ToString();
                    }
                    else
                    {
                        var searchTask = scrapper.ScrapeSearchPage(Game.Name, args.CancelToken);
                        searchTask.Wait(args.CancelToken);

                        var searchResult = searchTask.Result;
                        if (searchResult is null || !searchResult.Any())
                        {
                            F95ZoneMetadataProvider.Logger.Error($"Search returned nothing for {Game.Name}, make sure you are logged in!");
                            F95ZoneMetadataProvider.Api.Notifications.Add(
                                Guid.NewGuid().ToString(),
                                $"Search returned nothing for {Game.Name}. Please check the logs for more details.",
                                NotificationType.Error);
                            _didRun = true;
                            return null;
                        }

                        id = GetIdFromLink(searchResult.First().Link ?? string.Empty);
                    }

                    if (id is null)
                    {
                        F95ZoneMetadataProvider.Logger.Error($"Failed to get ID from search result for {Game.Name}.");
                        F95ZoneMetadataProvider.Api.Notifications.Add(
                            Guid.NewGuid().ToString(),
                            $"Failed to get ID from search result for {Game.Name}. Please check the logs for more details.",
                            NotificationType.Error);
                        _didRun = true;
                        return null;
                    }
                }
                else
                {
                    var item = F95ZoneMetadataProvider.Api.Dialogs.ChooseItemWithSearch(
                        new List<GenericItemOption>(),
                        searchString =>
                        {
                            var apiSearchTask = F95ZoneMetadataProvider.ApiService.GetGames(searchString);
                            apiSearchTask.Wait(args.CancelToken);
                            var apiResults = apiSearchTask.Result;

                            var items = new List<GenericItemOption>();
                            if (apiResults != null && apiResults.Any())
                            {
                                items.AddRange(apiResults.Select(x => new GenericItemOption(x.Title, x.ThreadId.ToString()) { Name = x.Title, Description = x.ThreadId.ToString() }));
                            }

                            // If API returned nothing or we want to provide more options, also scrape
                            if (!items.Any())
                            {
                                var searchTask = scrapper.ScrapeSearchPage(searchString, args.CancelToken);
                                searchTask.Wait(args.CancelToken);

                                var searchResult = searchTask.Result;
                                if (searchResult != null)
                                {
                                    items.AddRange(searchResult
                                        .Where(x => x.Name is not null && x.Link is not null)
                                        .Select(x => new GenericItemOption(x.Name, x.Link) { Name = x.Name, Description = x.Link }));
                                }
                            }

                            return items;
                        }, Game.Name, "Search F95zone");

                    if (item is null)
                    {
                        _didRun = true;
                        return null;
                    }

                    var linkOrId = item.Description;
                    if (string.IsNullOrEmpty(linkOrId))
                    {
                        _didRun = true;
                        return null;
                    }

                    id = GetIdFromLink(linkOrId);
                    if (id == null && int.TryParse(linkOrId, out _))
                    {
                        id = linkOrId;
                    }

                    if (id is null)
                    {
                        F95ZoneMetadataProvider.Logger.Error($"Failed to get ID from search result for {Game.Name}.");
                        F95ZoneMetadataProvider.Api.Notifications.Add(
                            Guid.NewGuid().ToString(),
                            $"Failed to get ID from search result for {Game.Name}. Please check the logs for more details.",
                            NotificationType.Error);
                        _didRun = true;
                        return null;
                    }
                }
            }

            // If we don't have _apiGame yet (e.g. ID was already in Game), try to get it from API
            if (_apiGame == null && id != null && int.TryParse(id, out int threadId))
            {
                var apiSearchTask = F95ZoneMetadataProvider.ApiService.GetGames(Game.Name);
                apiSearchTask.Wait(args.CancelToken);
                var apiResults = apiSearchTask.Result;
                _apiGame = apiResults?.FirstOrDefault(x => x.ThreadId == threadId);
            }

            var task = scrapper.ScrapePage(id, args.CancelToken);
            task.Wait(args.CancelToken);
            _result = task.Result;
            _didRun = true;

            if (_result != null && _apiGame != null)
            {
                // Apply API data where applicable
                if (!string.IsNullOrEmpty(_apiGame.Title))
                {
                    _result.Name = _apiGame.Title;
                }

                if (!string.IsNullOrEmpty(_apiGame.Version))
                {
                    _result.Version = _apiGame.Version;
                }

                if (!string.IsNullOrEmpty(_apiGame.Author))
                {
                    _result.Developer = _apiGame.Author;
                }

                if (_apiGame.Rating.HasValue)
                {
                    _result.Rating = _apiGame.Rating.Value;
                }
            }

            // TODO: there is no override function for this

            // TODO: there is no override function for this
            if (_result?.Version != null)
            {
                Game.Version = _result.Version;
            }

            return _result;
        }

        /// <summary>
        /// Retrieves the name for a metadata field, falling back to the base implementation if not found.
        /// </summary>
        /// <param name="args">The arguments containing metadata field information.</param>
        /// <returns>
        /// The metadata field name if available; otherwise, the name provided by the base implementation.
        /// </returns>
        public override string GetName(GetMetadataFieldArgs args)
        {
            return GetResult(args)?.Name ?? base.GetName(args);
        }

        /// <summary>
        /// Gets the description for the specified metadata field.
        /// If the metadata result is found and has a non-null <c>Description</c>,
        /// that value is returned; otherwise, the base implementation's name is returned.
        /// </summary>
        /// <param name="args">The arguments identifying which metadata field to retrieve.</param>
        /// <returns>
        /// The metadata description if available; otherwise, the base metadata field name.
        /// </returns>
        public override string GetDescription(GetMetadataFieldArgs args)
        {
            return GetResult(args)?.Description ?? base.GetName(args);
        }

        /// <summary>
        /// Retrieves the release date for the specified metadata field.
        /// </summary>
        /// <param name="args">The arguments containing metadata field information.</param>
        /// <returns>
        /// A <see cref="ReleaseDate"/> instance representing the release date if available; otherwise, <c>null</c>.
        /// </returns>
        public override ReleaseDate? GetReleaseDate(GetMetadataFieldArgs args)
        {
            var date = GetResult(args)?.ReleaseDate;
            return date.HasValue ? new ReleaseDate(date.Value) : base.GetReleaseDate(args);
        }

        /// <summary>
        /// Retrieves the developer metadata properties for a given metadata field argument.
        /// If the developer string is null, falls back to the base implementation.
        /// Otherwise, attempts to resolve the developer to a company record in the database,
        /// returning an ID property if found, or a name property otherwise.
        /// </summary>
        /// <param name="args">The arguments containing the metadata field information.</param>
        /// <returns>
        /// An enumerable of <see cref="MetadataProperty"/> instances representing
        /// the developer(s): either a <see cref="MetadataIdProperty"/> if the company
        /// was found by name, or a <see cref="MetadataNameProperty"/> with the raw name.
        /// </returns>
        public override IEnumerable<MetadataProperty> GetDevelopers(GetMetadataFieldArgs args)
        {
            var dev = GetResult(args)?.Developer;
            if (dev is null)
                return base.GetDevelopers(args);

            var company = F95ZoneMetadataProvider.Api.Database
                .Companies
                .Where(x => x.Name is not null)
                .FirstOrDefault(x => x.Name.Equals(dev, StringComparison.OrdinalIgnoreCase));

            if (company is not null)
                return new[] { new MetadataIdProperty(company.Id) };

            return new[] { new MetadataNameProperty(dev) };
        }

        /// <summary>
        /// Retrieves a sequence of <see cref="Link"/> objects for the specified metadata field arguments.
        /// If no valid ID is found in the result, the method falls back to the base implementation.
        /// A default link is always created and inserted at the start of the returned list.
        /// Scraping of additional links is controlled by the provider settings.
        /// </summary>
        /// <param name="args">An instance of <see cref="GetMetadataFieldArgs"/> containing the parameters for metadata retrieval.</param>
        /// <returns>
        /// An <see cref="IEnumerable{Link}"/> consisting of the default link and any additional links
        /// fetched from the result, or the base class links if no ID is present.
        /// </returns>
        public override IEnumerable<Link> GetLinks(GetMetadataFieldArgs args)
        {
            var result = GetResult(args);
            var id = result?.Id;
            var fetchedLinks = result?.Links;
            if (id == null)
            {
                return base.GetLinks(args);
            }

            Link defaultLink = new Link("F95zone", Scraper.DefaultBaseUrl + id);

            if (fetchedLinks == null || !F95ZoneMetadataProvider.Settings.ShouldScrapeLinks)
            {
                return new[] { defaultLink };
            }

            fetchedLinks.Insert(0, defaultLink);
            return fetchedLinks;
        }

        /// <summary>
        /// Retrieves metadata properties for tags and labels based on the specified metadata field arguments
        /// and the current Playnite property.
        /// </summary>
        /// <param name="args">The arguments specifying the metadata field retrieval parameters.</param>
        /// <param name="currentProperty">The current Playnite property context.</param>
        /// <returns>
        /// A combined enumeration of <see cref="MetadataProperty"/> objects for tags and labels,
        /// or <c>null</c> if no properties are available.
        /// </returns>
        private IEnumerable<MetadataProperty>? GetProperties(GetMetadataFieldArgs args, PlayniteProperty currentProperty)
        {
            // Tags
            var tagProperties = PlaynitePropertyHelper.ConvertValuesIfPossible(
                F95ZoneMetadataProvider.Api,
                F95ZoneMetadataProvider.Settings.TagProperty,
                currentProperty,
                () => GetResult(args)?.Tags);

            // Labels
            var labelProperties = PlaynitePropertyHelper.ConvertValuesIfPossible(
                F95ZoneMetadataProvider.Api,
                F95ZoneMetadataProvider.Settings.LabelProperty,
                currentProperty,
                () => GetResult(args)?.Labels);

            return PlaynitePropertyHelper.MultiConcat(tagProperties, labelProperties);
        }

        /// <summary>
        /// Retrieves the metadata properties representing tags for the specified metadata field arguments.
        /// </summary>
        /// <param name="args">Arguments that specify which metadata field to retrieve tags for.</param>
        /// <returns>
        /// A collection of <see cref="MetadataProperty"/> objects representing tags.
        /// If no custom tag properties are found, returns the base implementation's tags.
        /// </returns>
        public override IEnumerable<MetadataProperty> GetTags(GetMetadataFieldArgs args)
        {
            return GetProperties(args, PlayniteProperty.Tags) ?? base.GetTags(args);
        }

        /// <summary>
        /// Retrieves metadata feature properties for the specified metadata field arguments.
        /// If no custom feature properties are found, the base class implementation is invoked.
        /// </summary>
        /// <param name="args">Arguments specifying the metadata field to retrieve features for.</param>
        /// <returns>
        /// A collection of <see cref="MetadataProperty"/> objects representing the features,
        /// or the result from the base class implementation if none are found.
        /// </returns>
        public override IEnumerable<MetadataProperty> GetFeatures(GetMetadataFieldArgs args)
        {
            return GetProperties(args, PlayniteProperty.Features) ?? base.GetFeatures(args);
        }

        /// <summary>
        /// Retrieves the genre metadata properties for an item.
        /// </summary>
        /// <param name="args">The arguments containing metadata retrieval settings.</param>
        /// <returns>
        /// An <see cref="IEnumerable{MetadataProperty}"/> of genre metadata properties.
        /// Falls back to the base implementation if no custom properties are found.
        /// </returns>
        public override IEnumerable<MetadataProperty> GetGenres(GetMetadataFieldArgs args)
        {
            return GetProperties(args, PlayniteProperty.Genres) ?? base.GetGenres(args);
        }

        /// <summary>
        /// Calculates the community score based on the rating obtained from the provided metadata arguments.
        /// If the rating is null or NaN, this method defers to the base implementation.
        /// Otherwise, it scales a 0–5 rating to a 0–100 percentage.
        /// </summary>
        /// <param name="args">The metadata field arguments used to retrieve the rating.</param>
        /// <returns>
        /// A nullable integer representing the community score percentage,
        /// or the result from the base implementation if the rating is null or NaN.
        /// </returns>
        public override int? GetCommunityScore(GetMetadataFieldArgs args)
        {
            var rating = GetResult(args)?.Rating;
            return rating switch
            {
                null => base.GetCommunityScore(args),
                double.NaN => base.GetCommunityScore(args),
                _ => (int)(rating.Value / 5 * 100)
            };
        }

        /// <summary>
        /// Selects an image from the metadata based on the provided arguments and caption.
        /// If running in background download mode, returns the first available image without user interaction.
        /// Otherwise, presents a dialog to the user to choose an image file.
        /// </summary>
        /// <param name="args">The arguments containing metadata fields used to retrieve images.</param>
        /// <param name="caption">The caption to display in the image selection dialog.</param>
        /// <returns>
        /// A <see cref="MetadataFile"/> representing the selected image, or <c>null</c> if no images are available
        /// or the user cancels the selection.
        /// </returns>
        private MetadataFile? SelectImage(GetMetadataFieldArgs args, string caption)
        {
            var images = GetResult(args)?.Images;
            if (images is null || !images.Any())
                return null;

            if (IsBackgroundDownload)
            {
                // In background mode, automatically take the first image
                return new MetadataFile(images.First());
            }

            // Show dialog for user to choose an image file
            var imageFileOption = F95ZoneMetadataProvider.Api.Dialogs
                .ChooseImageFile(images.Select(image => new ImageFileOption(image)).ToList(), caption);

            return imageFileOption == null
                ? null
                : new MetadataFile(imageFileOption.Path);
        }

        /// <summary>
        /// Retrieves the cover image metadata file based on the provided arguments.
        /// </summary>
        /// <param name="args">The arguments containing context for selecting the metadata field.</param>
        /// <returns>
        /// A <see cref="MetadataFile"/> representing the selected cover image,
        /// or <c>null</c> if no image was selected.
        /// </returns>
        public override MetadataFile? GetCoverImage(GetMetadataFieldArgs args)
        {
            return SelectImage(args, "Select Cover Image");
        }

        /// <summary>
        /// Retrieves the background image based on the provided metadata field arguments.
        /// </summary>
        /// <param name="args">Arguments containing metadata information used to select the image.</param>
        /// <returns>
        /// A <see cref="MetadataFile"/> representing the selected background image,
        /// or <c>null</c> if no image was selected.
        /// </returns>
        public override MetadataFile? GetBackgroundImage(GetMetadataFieldArgs args)
        {
            return SelectImage(args, "Select Background Image");
        }

        /// <summary>
        /// Retrieves the icon to use for a metadata field.
        /// If the provider is configured to use the default icon, returns a new <see cref="MetadataFile"/>
        /// initialized with <see cref="IconUrl"/>; otherwise calls the base implementation.
        /// </summary>
        /// <param name="args">The arguments that define the metadata field for which the icon is requested.</param>
        /// <returns>
        /// A <see cref="MetadataFile"/> representing the icon to be used for the specified metadata field.
        /// </returns>
        public override MetadataFile GetIcon(GetMetadataFieldArgs args)
        {
            return F95ZoneMetadataProvider.Settings.SetDefaultIcon
                ? new MetadataFile(IconUrl)
                : base.GetIcon(args);
        }
    }
}