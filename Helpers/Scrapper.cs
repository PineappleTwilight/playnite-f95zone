using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Dom.Html;
using AngleSharp.Html;
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

#nullable enable

namespace F95ZoneMetadataProvider
{
    public class Scrapper
    {
        public const string DefaultBaseUrl = "https://f95zone.to/threads/";

        private const string CoverLinkPrefix = "https://f95zone.to/data/covers";
        private const string ImageLinkPrefix = "https://attachments.f95zone.to/";

        private readonly string _baseUrl;
        private TextInfo textInfo = new CultureInfo("en-US", false).TextInfo;

        private readonly ILogger /*<Scrapper>*/
            _logger;

        private HttpClientHandler _handler;
        private readonly IConfiguration _configuration;

        private HttpClient httpClient;

        public Scrapper(ILogger /*<Scrapper>*/ logger, HttpClientHandler messageHandler,
            string baseUrl = DefaultBaseUrl)
        {
            _logger = logger;
            _baseUrl = baseUrl;

            _configuration = Configuration.Default
                .WithDefaultLoader();
            _handler = messageHandler;
            httpClient = new HttpClient(_handler);
        }

        private DateTime? ParseUnknownDate(string date)
        {
            if (!DateTime.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed))
            {
                return null;
            }

            return parsed;
        }

        /// <summary>
        /// Handles potential DDOS protection checks and attempts to bypass them if detected.
        /// </summary>
        /// <remarks>This method checks for common DDOS protection mechanisms, such as specific HTML
        /// elements or text patterns, and attempts to bypass them using a JavaScript-enabled WebView. If bypassing
        /// fails or login cookies are invalid, the operation is aborted, and appropriate error messages are logged and
        /// displayed to the user.</remarks>
        /// <param name="url">The URL of the page being processed.</param>
        /// <param name="document">The initial document to analyze for DDOS protection indicators.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>The processed <see cref="IDocument"/> after handling DDOS checks, or <see langword="null"/> if the operation
        /// fails due to DDOS protection, invalid login cookies, or other blocking conditions.</returns>
        private async Task<IDocument?> HandleDdosChecks(string url, IDocument document, CancellationToken cancellationToken)
        {
            if (document is null || document.Source.Text == string.Empty)
            {
                _logger.Error("Document is null, scraping aborted.");
                return null;
            }

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

                // Import cookies from the handler's cookie container
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

                // Get the page source
                var pageSource = await Application.Current.Dispatcher.InvokeAsync(() => webView.GetPageSource());

                // Throw error on weird tracker/adware page
                if (pageSource.Contains("AdGlareDisplayAd"))
                {
                    _logger.Warn("AdGlare redirect detected, scraping aborted.");
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
                    _logger.Error("DDOS Protection detected after implementing bypass, scraping aborted.");
                    F95ZoneMetadataProvider.Api.Dialogs.ShowErrorMessage(
                        "DDOS Protection detected after implementing bypass, scraping aborted. Please try again later or turn on a VPN.",
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

            _logger.Debug("Scraping page " + _baseUrl + id + " with " + _handler.CookieContainer.Count + " cookie(s).");

            var response = await httpClient.GetAsync(_baseUrl + id, cancellationToken);
            var httpContent = await response.Content.ReadAsStringAsync();

            var context = BrowsingContext.New(_configuration);
            var document = await context.OpenAsync(req => req.Content(httpContent), cancellationToken);

            document = await HandleDdosChecks(_baseUrl + id, document, cancellationToken);

            if (document is null)
            {
                _logger.Error("Document is null after DDOS check, scraping aborted.");
                F95ZoneMetadataProvider.Api.Dialogs.ShowErrorMessage(
                    "Unable to scrape page, document is null after DDOS check. Please try again later.",
                    "Scraping Error");
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
                    .Select(elem => elem.TextContent.Trim())
                    .Where(text => !string.IsNullOrWhiteSpace(text))
                    .ToList();

                var title = titleElement.TextContent.Trim();
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
                scrapeResult.Name = name?.Trim();
                scrapeResult.Version = version?.Trim();
                scrapeResult.Developer = developer?.Trim();
                scrapeResult.Description = description?.Replace("Overview:", string.Empty).Replace("Spoiler:", string.Empty).Trim();

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
                    .Select(elem => elem.TextContent)
                    .Where(t => t is not null && !string.IsNullOrWhiteSpace(t))
                    .ToList();

                List<string> sanitizedTags = new List<string>();
                foreach (var tag in tags)
                {
                    // Remove any HTML tags and trim whitespace
                    var sanitizedTag = Regex.Replace(tag, "<.*?>", string.Empty).Trim();
                    if (!string.IsNullOrWhiteSpace(sanitizedTag))
                    {
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
                _logger.Warn("Unable to find element with name \"rating\" using fallback, make sure you are logged in.");

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
                        _logger.Warn("Rating element does not have a \"title\" attribute!");
                    }
                }
                else
                {
                    _logger.Warn("Unable to find element with class \"bratr-rating\".");
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
                _logger.Warn("Unable to find elements with class \"message-content\".");
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
                .Where(link => !string.IsNullOrEmpty(link.Url) && !string.IsNullOrWhiteSpace(link.Url))
                .Where(link => !string.IsNullOrEmpty(link.Name) && !string.IsNullOrWhiteSpace(link.Name))
                .GroupBy(link => link.Url?.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

            int extrasCount = 0;
            foreach (var link in scrapeResult.Links)
            {
                // If the link name is the url, set the name to the domain name
                if (link.Url.Equals(link.Name, StringComparison.OrdinalIgnoreCase))
                {
                    // If the URL is the same as the name, set the name to the domain name
                    Url uri = new Url(link.Url);
                    string[] strings = uri.Host.Split('.');
                    if (strings.Length >= 2)
                    {
                        // If the URL has a subdomain, use the last part
                        link.Name = $"{textInfo.ToTitleCase(strings[strings.Length - 2])}";
                    }
                    else
                    {
                        // Otherwise, use the whole host
                        link.Name = textInfo.ToTitleCase(uri.Host);
                    }
                }

                // If the link name is "Website", set it to "Official Website"
                if (link.Name.Equals("Website", StringComparison.OrdinalIgnoreCase))
                {
                    link.Name = "Official Website";
                }

                // If the link name is "Here", set it to "Extra Info #1", "Extra Info #2", etc.
                if (link.Name.Equals("Here", StringComparison.OrdinalIgnoreCase) || link.Name.Equals("Link", StringComparison.OrdinalIgnoreCase) || link.Name.Equals("This", StringComparison.OrdinalIgnoreCase))
                {
                    link.Name = "Related Info #" + (extrasCount + 1);
                    extrasCount++;
                }

                // If the first character is lowercase, convert the whole string to title case
                if (char.IsLower(link.Name[0]))
                {
                    link.Name = textInfo.ToTitleCase(link.Name);
                }
            }

            return scrapeResult;
        }

        /// <summary>
        /// Scrapes the search results page for the specified search term and retrieves a list of results.
        /// </summary>
        /// <remarks>This method sends an HTTP request to the target website's search page, processes the
        /// response,  and extracts relevant search result data. It handles potential redirects and performs checks  for
        /// anti-bot mechanisms (e.g., DDOS protection). If the document cannot be processed,  the method logs an error
        /// and returns an empty list.</remarks>
        /// <param name="term">The search term to query on the target website.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests. The operation will be canceled if the token is triggered.</param>
        /// <returns>A list of <see cref="ScrapperSearchResult"/> objects representing the search results.  Returns an empty list
        /// if no results are found or if an error occurs during the scraping process.</returns>
        public async Task<List<ScrapperSearchResult>> ScrapSearchPage(string term,
            CancellationToken cancellationToken = default)
        {
            var url = $"https://f95zone.to/search/{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}/?q={term}&t=post&c[child_nodes]=1&c[nodes][0]=2&o=relevance&g=1";

            var response = await httpClient.GetAsync(url, cancellationToken);
            var httpContent = await response.Content.ReadAsStringAsync();

            var context = BrowsingContext.New(_configuration);
            var document = await context.OpenAsync(req => req.Content(httpContent));

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
                var title = anchorElement.TextContent.Trim();
                var name = GetNameOfSearchResult(title);

                results.Add(new ScrapperSearchResult
                {
                    Link = link,
                    Name = name,
                });
            }

            return results;
        }

        /// <summary>
        /// Attempts to extract and parse a numeric rating from the beginning of the specified text.
        /// </summary>
        /// <remarks>The method expects the input string to start with a numeric value followed by a
        /// space. If the format is invalid or parsing fails, the method returns <see langword="false"/> and sets
        /// <paramref name="rating"/> to <see cref="double.NaN"/>.</remarks>
        /// <param name="text">The input string containing the rating as a numeric value followed by a space.</param>
        /// <param name="rating">When this method returns, contains the parsed numeric rating if the operation succeeds; otherwise, contains
        /// <see cref="double.NaN"/>.</param>
        /// <returns><see langword="true"/> if a numeric rating is successfully parsed from the input text; otherwise, <see
        /// langword="false"/>.</returns>
        public static bool GetRating(string text, out double rating)
        {
            rating = double.NaN;

            var spaceIndex = text.IndexOf(' ');
            if (spaceIndex == -1) return false;

            var sDouble = text.Substring(0, spaceIndex);
            return NumberExtensions.TryParse(sDouble, out rating);
        }

        /// <summary>
        /// Extracts the name, version, and developer information from a formatted title string.
        /// </summary>
        /// <remarks>This method assumes the input string follows a specific format with components
        /// enclosed in square brackets. If the format is not adhered to, missing components will be returned as <see
        /// langword="null"/>.</remarks>
        /// <param name="title">A string containing the title information, typically formatted as  "Name [Version] [Developer]". For
        /// example, "Corrupted Kingdoms [v0.12.8] [ArcGames]".</param>
        /// <returns>A tuple containing the extracted components: <list type="bullet"> <item><description><c>Name</c>: The name
        /// of the title, or <see langword="null"/> if not found.</description></item>
        /// <item><description><c>Version</c>: The version of the title, or <see langword="null"/> if not
        /// found.</description></item> <item><description><c>Developer</c>: The developer of the title, or <see
        /// langword="null"/> if not found.</description></item> </list> If the input string is empty, the method
        /// returns <see langword="default"/>.</returns>
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

        /// <summary>
        /// Extracts the main name of a search result by removing any bracketed content (e.g., "[...]").
        /// </summary>
        /// <remarks>This method processes the input string by iteratively removing all substrings
        /// enclosed in square brackets ('[' and ']')  until no such substrings remain. The resulting string is trimmed
        /// of any leading or trailing whitespace.</remarks>
        /// <param name="title">The title of the search result, which may contain bracketed content.</param>
        /// <returns>A string representing the title with all bracketed content removed.  If no bracketed content is found, the
        /// original <paramref name="title"/> is returned.</returns>
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