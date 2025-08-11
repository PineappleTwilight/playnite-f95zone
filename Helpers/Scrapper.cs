using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace F95ZoneMetadataProvider
{
    public class Scrapper
    {
        private const string CoverLinkPrefix = "https://f95zone.to/data/covers";
        private const string ImageLinkPrefix = "https://attachments.f95zone.to/";

        public const string DefaultBaseUrl = "https://f95zone.to/threads/";
        private readonly string _baseUrl;

        private readonly ILogger /*<Scrapper>*/
            _logger;

        private HttpClientHandler _handler;
        private readonly IConfiguration _configuration;

        public Scrapper(ILogger /*<Scrapper>*/ logger, HttpClientHandler messageHandler,
            string baseUrl = DefaultBaseUrl)
        {
            _logger = logger;
            _baseUrl = baseUrl;

            _configuration = Configuration.Default
                .WithRequesters(messageHandler)
                .WithCulture(CultureInfo.InvariantCulture)
                .WithDefaultLoader();
            _handler = messageHandler;
        }

        private DateTime? ParseUnknownDate(string date)
        {
            if (!DateTime.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed))
            {
                return null;
            }

            return parsed;
        }

        private async Task<IDocument> HandleDdosChecks(string url, IDocument document, CancellationToken cancellationToken)
        {
            if (document is null || document.Source.Text == string.Empty)
            {
                _logger.Error("Document is null, scraping aborted.");
                return null;
            }

            _logger.Debug(document.Source.Text);

            // Check for DDOS page
            var ddosProtectionElement = document.GetElementsByClassName("ddg-captcha").FirstOrDefault();
            bool ddosProtectionString = document.Source.Text.Contains("Checking your browser before accessing f95zone.to");
            bool ddosProtectionString2 = document.Source.Text.Contains("Sorry, but this looks too much like a bot request.");
            bool loginFailString = document.Source.Text.Contains("Sorry, you have to be");
            if (ddosProtectionElement is not null || ddosProtectionString || document.Title.ToLower() == "ddos-guard" || ddosProtectionString2)
            {
                // Attempt JS-enabled workaround

                // Create the WebView on the UI thread
                var webView = await Application.Current.Dispatcher.InvokeAsync(() =>
                    F95ZoneMetadataProvider.Api.WebViews.CreateView(new WebViewSettings
                    {
                        UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:128.0) Gecko/20100101 Firefox/128.0",
                        JavaScriptEnabled = true,
                        WindowWidth = 900,
                        WindowHeight = 700,
                    }));

                // Set cookies (if this is UI-thread safe, otherwise do it outside)
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    foreach (Cookie cookie in _handler.CookieContainer.GetCookies(new Uri(url)))
                    {
                        string path = string.IsNullOrEmpty(cookie.Path) ? "/" : cookie.Path;
                        DateTime expires = cookie.Expires != DateTime.MinValue ? cookie.Expires : DateTime.MaxValue;

                        webView.SetCookies(
                            url,
                            cookie.Domain,
                            cookie.Name,
                            cookie.Value,
                            path,
                            expires
                        );
                    }
                });

                // Open the WebView and navigate
                await Application.Current.Dispatcher.InvokeAsync(() => webView.Open());
                await Application.Current.Dispatcher.InvokeAsync(() => webView.NavigateAndWait(url));

                // Get the page source (if this is a UI operation)
                var pageSource = await Application.Current.Dispatcher.InvokeAsync(() => webView.GetPageSource());

                // Weird tracker page
                if (pageSource.Contains("AdGlareDisplayAd"))
                {
                    _logger.Warn("AdGlare detected, scraping aborted.");
                    F95ZoneMetadataProvider.Api.Dialogs.ShowErrorMessage(
                        "AdGlare detected, scraping aborted. Please try again later.",
                        "AdGlare Detected");
                    return null;
                }

                document = await BrowsingContext.New(_configuration)
                    .OpenAsync(req => req.Content(pageSource ?? string.Empty), cancellationToken);

                // Close the WebView to ensure we release resources
                await Application.Current.Dispatcher.InvokeAsync(() => webView.Close());
                await Application.Current.Dispatcher.InvokeAsync(() => webView.Dispose());

                _logger.Info(document.Source.Text);

                // Refresh ddos values after navigating
                ddosProtectionElement = document.GetElementsByClassName("ddg-captcha").FirstOrDefault();
                ddosProtectionString = document.Source.Text.Contains("Checking your browser before accessing f95zone.to");
                ddosProtectionString2 = document.Source.Text.Contains("Sorry, but this looks too much like a bot request.");
                loginFailString = document.Source.Text.Contains("Sorry, you have to be");
                if (ddosProtectionElement is not null || ddosProtectionString || document.Title.ToLower() == "ddos-guard" || ddosProtectionString2)
                {
                    _logger.Error("DDOS Protection detected, scraping aborted.");
                    F95ZoneMetadataProvider.Api.Dialogs.ShowErrorMessage(
                        "DDOS Protection detected, scraping aborted. Please try again later.",
                        "DDOS Protection Detected");
                    return null;
                }
            }

            if (loginFailString)
            {
                _logger.Error("Login cookies invalid, scraping aborted.");
                F95ZoneMetadataProvider.Api.Dialogs.ShowErrorMessage(
                    "Login cookies invalid, scraping aborted. Please re-authenticate.",
                    "Login Failed");
                return null;
            }
            return document;
        }

        /// <summary>
        /// Scrapes a web page for specific content and metadata based on the provided identifier.
        /// </summary>
        /// <remarks>This method retrieves and parses the HTML content of a web page to extract various
        /// elements such as the title, description, tags, rating, images, and links. If certain elements are not found,
        /// warnings are logged, and the corresponding properties in the result may be <see langword="null"/> or default
        /// values.</remarks>
        /// <param name="id">The unique identifier of the page to scrape. This is appended to the base URL to form the full page URL.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests. Defaults to <see cref="CancellationToken.None"/>.</param>
        /// <returns>A <see cref="ScrapperResult"/> containing the scraped content and metadata, or <see langword="null"/> if the
        /// page content could not be found.</returns>
        public async Task<ScrapperResult?> ScrapPage(string id, CancellationToken cancellationToken = default)
        {
            var scrapeResult = new ScrapperResult
            {
                Id = id
            };

            // Build custom HTTP client
            var HttpClient = new HttpClient(_handler);

            _logger.Info("Sending request to " + _baseUrl + id + " with " + _handler.CookieContainer.Count + " cookie(s).");

            var response = await HttpClient.GetAsync(_baseUrl + id, cancellationToken);
            if (response.IsSuccessStatusCode == false)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.MovedPermanently)
                {
                    response = await HttpClient.GetAsync(response.Headers.Location, cancellationToken);
                }
                else
                {
                    _logger.Error($"Failed to fetch page {_baseUrl + id}: {response.ReasonPhrase}");
                    F95ZoneMetadataProvider.Api.Dialogs.ShowErrorMessage(
                        $"Failed to fetch page {_baseUrl + id}: {response.ReasonPhrase}",
                        "Scraping Error");
                    return null;
                }
            }

            var webContent = await response.Content.ReadAsStringAsync();
            var document = await BrowsingContext.New(_configuration).OpenAsync(req => req.Content(webContent));

            document = await HandleDdosChecks(_baseUrl + id, document, cancellationToken);

            if (document is null)
            {
                _logger.Error("Document is null after DDOS check, scraping aborted.");
                return null;
            }

            // The description is usually the first child of the bbWrapper div
            var description = document.QuerySelector(".bbWrapper > div:nth-child(1)")?.TextContent?.Trim();
            if (string.IsNullOrWhiteSpace(description))
            {
                _logger.Warn("Unable to find description element, using fallback");
                description = "No description.";
            }

            // Title
            var titleElement = document.GetElementsByClassName("p-title-value").FirstOrDefault();
            if (titleElement is not null)
            {
                var labels = titleElement
                    .GetElementsByClassName("labelLink")
                    .Select(elem => elem.Text().Trim())
                    .Where(text => !string.IsNullOrWhiteSpace(text))
                    .ToList();

                var title = titleElement.Text().Trim();
                if (labels.Any())
                {
                    var lastLabel = labels.Last();
                    var labelIndex = title.IndexOf(lastLabel, StringComparison.OrdinalIgnoreCase);

                    if (labelIndex != -1)
                    {
                        title = title.Substring(labelIndex + lastLabel.Length + 1).Trim();
                    }
                }

                var (name, version, developer) = TitleBreakdown(title);
                scrapeResult.Name = name.Trim();
                scrapeResult.Version = version.Trim();
                scrapeResult.Developer = developer.Trim();
                scrapeResult.Description = description.Replace("Overview:", string.Empty).Trim();

                scrapeResult.Labels = labels.Any() ? labels : null;
            }
            else
            {
                _logger.Warn("Unable to find element with class \"p-title-value\"");
            }

            // Tags
            var tagItemElements = document.GetElementsByClassName("tagItem")
                .Where(elem => elem.TagName.Equals(TagNames.A, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (tagItemElements.Any())
            {
                var tags = tagItemElements
                    .Select(elem => elem.Text())
                    .Where(t => t is not null && !string.IsNullOrWhiteSpace(t))
                    .ToList();

                List<string> sanitizedTags = new List<string>();
                foreach (var tag in tags)
                {
                    // Remove any HTML tags and trim whitespace
                    var sanitizedTag = Regex.Replace(tag, "<.*?>", string.Empty).Trim();
                    if (!string.IsNullOrWhiteSpace(sanitizedTag))
                    {
                        // Convert to title case
                        TextInfo textInfo = new CultureInfo("en-US", false).TextInfo;

                        sanitizedTag = textInfo.ToTitleCase(sanitizedTag);
                        sanitizedTags.Add(sanitizedTag);
                    }
                }
                scrapeResult.Tags = tags.Any() ? sanitizedTags : null;
            }
            else
            {
                _logger.Warn("Unable to find Elements with class \"tagItem\"");
            }

            // Rating
            var selectRatingElement = (IHtmlSelectElement?)document.GetElementsByName("rating")
                .FirstOrDefault(elem => elem.TagName.Equals(TagNames.Select, StringComparison.OrdinalIgnoreCase));
            if (selectRatingElement is not null)
            {
                if (selectRatingElement.Dataset.Any(x =>
                        x.Key.Equals("initial-rating", StringComparison.OrdinalIgnoreCase)))
                {
                    var kv = selectRatingElement.Dataset.FirstOrDefault(x =>
                        x.Key.Equals("initial-rating", StringComparison.OrdinalIgnoreCase));
                    if (NumberExtensions.TryParse(kv.Value, out var rating))
                    {
                        scrapeResult.Rating = rating;
                    }
                    else
                    {
                        _logger.Warn($"Unable parse \"{kv.Value}\" as double");
                    }
                }
                else
                {
                    _logger.Warn(
                        "Element with name \"rating\" does not have a data value with the name \"initial-rating\"");
                }
            }
            else
            {
                _logger.Warn("Unable to find Element with name \"rating\" using fallback, make sure you are logged in");

                var ratingElement = document.GetElementsByClassName("bratr-rating").FirstOrDefault();
                if (ratingElement is not null)
                {
                    var titleAttribute = ratingElement.GetAttribute("title");
                    if (titleAttribute is not null)
                    {
                        if (!GetRating(titleAttribute, out var rating))
                        {
                            _logger.Warn($"Unable to get convert \"{titleAttribute}\" to a rating");
                        }
                        else
                        {
                            scrapeResult.Rating = rating;
                        }
                    }
                    else
                    {
                        _logger.Warn("Rating Element does not have a \"title\" Attribute!");
                    }
                }
                else
                {
                    _logger.Warn("Unable to find Element with class \"bratr-rating\"");
                }
            }

            // Images
            var messageBodyElements = document.GetElementsByClassName("message-body");
            if (messageBodyElements.Any())
            {
                var mainMessage = messageBodyElements.First();

                var images = new List<string>();
                var imageElements = mainMessage.GetElementsByTagName(TagNames.Img);
                foreach (var elem in imageElements)
                {
                    var imageElement = (IHtmlImageElement)elem;
                    if (imageElement.Source is null) continue;
                    if (!imageElement.Source.StartsWith(ImageLinkPrefix, StringComparison.OrdinalIgnoreCase)) continue;

                    var anchorElement = (IHtmlAnchorElement?)(
                        elem.ParentElement!.TagName.Equals(TagNames.NoScript, StringComparison.OrdinalIgnoreCase)
                            ? elem.ParentElement!.ParentElement!.TagName.Equals(TagNames.A,
                                StringComparison.OrdinalIgnoreCase)
                                ? elem.ParentElement.ParentElement
                                : null
                            : elem.ParentElement.TagName.Equals(TagNames.A, StringComparison.OrdinalIgnoreCase)
                                ? elem.ParentElement
                                : null);

                    if (anchorElement is not null)
                    {
                        images.Add(anchorElement.Href.StartsWith(ImageLinkPrefix)
                            ? anchorElement.Href
                            : imageElement.Source);
                    }
                    else
                    {
                        images.Add(imageElement.Source);
                    }
                }

                scrapeResult.Images = images.Any() ? images : null;
            }
            else
            {
                _logger.Warn("Unable to find Elements with class \"message-content\"");
            }

            // cover image
            var openGraphImageElement = document.Head?.GetElementsByTagName(TagNames.Meta)
                .Cast<IHtmlMetaElement>()
                .FirstOrDefault(elem => elem.GetAttribute("property") == "og:image");

            if (openGraphImageElement is not null)
            {
                var content = openGraphImageElement.Content;
                if (content is not null && !string.IsNullOrWhiteSpace(content))
                {
                    if (content.StartsWith(CoverLinkPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        scrapeResult.Images ??= new List<string>();
                        scrapeResult.Images.Insert(0, content);
                    }
                }
            }

            // Links
            var links = document.QuerySelectorAll(".message-threadStarterPost div.bbWrapper > a");

            scrapeResult.Links = links
                .Select(elem => new Link(elem.TextContent, elem.GetAttribute("href")))
                .Where(link => !string.IsNullOrWhiteSpace(link.Url))
                .Where(link => link.Name != link.Url)
                .GroupBy(link => link.Url?.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

            return scrapeResult;
        }

        public async Task<List<ScrapperSearchResult>> ScrapSearchPage(string term,
            CancellationToken cancellationToken = default)
        {
            var url = $"https://f95zone.to/search/{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}/?q={term}&t=post&c[child_nodes]=1&c[nodes][0]=2&o=relevance&g=1";

            var HttpClient = new HttpClient(_handler);

            _logger.Debug("Sending request to " + url + " with " + _handler.CookieContainer.Count + " cookie(s).");

            var response = await HttpClient.GetAsync(url, cancellationToken);
            if (response.IsSuccessStatusCode == false)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.MovedPermanently)
                {
                    response = await HttpClient.GetAsync(response.Headers.Location, cancellationToken);
                }
                else
                {
                    _logger.Error($"Failed to fetch search page {url}: {response.ReasonPhrase}");
                    F95ZoneMetadataProvider.Api.Dialogs.ShowErrorMessage(
                        $"Failed to fetch search page {url}: {response.ReasonPhrase}",
                        "Scraping Error");
                    return new List<ScrapperSearchResult>();
                }
            }

            var webContent = await response.Content.ReadAsStringAsync();
            var document = await BrowsingContext.New(_configuration).OpenAsync(req => req.Content(webContent));

            document = await HandleDdosChecks(url, document, cancellationToken);

            if (document is null)
            {
                _logger.Error("Document is null after DDOS check, scraping aborted.");
                return new List<ScrapperSearchResult>();
            }

            var blockRows = document.GetElementsByClassName("block-row")
                .Where(elem => elem.TagName.Equals(TagNames.Li, StringComparison.OrdinalIgnoreCase))
                .Cast<IHtmlListItemElement>()
                .Where(li => li.Dataset.Any(x => x.Key.Equals("author", StringComparison.OrdinalIgnoreCase)))
                .ToList();

            var results = new List<ScrapperSearchResult>();
            foreach (var blockRow in blockRows)
            {
                var headerElement = blockRow.GetElementsByClassName("contentRow-title").FirstOrDefault();
                if (headerElement is null) continue;

                var anchorElement = (IHtmlAnchorElement?)headerElement.Children.FirstOrDefault(x =>
                    x.TagName.Equals(TagNames.A, StringComparison.OrdinalIgnoreCase));
                if (anchorElement is null || string.IsNullOrWhiteSpace(anchorElement.Href)) continue;

                var link = anchorElement.Href;
                var title = anchorElement.Text().Trim();
                var name = GetNameOfSearchResult(title);

                results.Add(new ScrapperSearchResult
                {
                    Link = link,
                    Name = name,
                });

                // TODO: maybe add ratings or something
            }

            return results;
        }

        public static bool GetRating(string text, out double rating)
        {
            rating = double.NaN;

            var spaceIndex = text.IndexOf(' ');
            if (spaceIndex == -1) return false;

            var sDouble = text.Substring(0, spaceIndex);
            return NumberExtensions.TryParse(sDouble, out rating);
        }

        public static (string? Name, string? Version, string? Developer) TitleBreakdown(string title)
        {
            if (title.Equals(string.Empty)) return default;

            // "Corrupted Kingdoms [v0.12.8] [ArcGames]"

            var span = title.AsSpan().Trim();

            var bracketStartIndex = span.IndexOf('[');
            var bracketEndIndex = span.IndexOf(']');

            if (bracketStartIndex == -1 || bracketEndIndex == -1)
            {
                return (title, null, null);
            }

            // "Corrupted Kingdoms"
            var nameSpan = span.Slice(0, bracketStartIndex - 1).Trim();

            // "v0.12.8"
            var versionSpan = span.Slice(bracketStartIndex + 1, bracketEndIndex - bracketStartIndex - 1).Trim();

            span = span.Slice(bracketEndIndex + 1);
            bracketStartIndex = span.IndexOf('[');
            bracketEndIndex = span.IndexOf(']');

            if (bracketStartIndex == -1 || bracketEndIndex == -1)
            {
                return (nameSpan.ToString(), versionSpan.ToString(), null);
            }

            // "ArcGames"
            var developerSpan = span.Slice(bracketStartIndex + 1, bracketEndIndex - bracketStartIndex - 1).Trim();

            return (nameSpan.ToString(), versionSpan.ToString(), developerSpan.ToString());
        }

        public static string GetNameOfSearchResult(string title)
        {
            var span = title.AsSpan().Trim();

            // [Flash] [Completed] Corruption of Champions [Fenoxo]
            // [Others] Corruption of Champions II [v0.4.28] [Savin/Salamander Studios]

            var bracketStartIndex = span.IndexOf('[');
            var bracketEndIndex = span.IndexOf(']');

            if (bracketStartIndex == -1 || bracketEndIndex == -1) return title;

            do
            {
                span = bracketStartIndex == 0
                    ? span.Slice(bracketEndIndex + 1).Trim()
                    : span.Slice(0, bracketStartIndex - 1).Trim();

                bracketStartIndex = span.IndexOf('[');
                bracketEndIndex = span.IndexOf(']');
            } while (bracketStartIndex != -1 && bracketEndIndex != -1);

            return span.Trim().ToString();
        }
    }
}