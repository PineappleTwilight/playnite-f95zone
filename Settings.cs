using AngleSharp.Text;
using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Events;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace F95ZoneMetadataProvider
{
    public class F95ZoneCookie
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public F95ZoneCookie(string name, string value)
        {
            Name = name;
            Value = value;
        }
    }

    public class Settings : ISettings, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private const string LoginUrl = "https://f95zone.to/login";

        private readonly Plugin? _plugin;
        private readonly IPlayniteAPI? _playniteAPI;

        /// <summary>
        /// Gets or sets the collection of DDoS Guard cookies.
        /// </summary>
        /// <remarks>This property is used to store cookies required for interacting with services
        /// protected by DDoS Guard. Each tuple represents a cookie, with the first item being the cookie name and the
        /// second item being the cookie value.</remarks>
        public ObservableCollection<F95ZoneCookie> ZoneCookies
        {
            get => _zoneCookies;
            set
            {
                if (_zoneCookies != null)
                    _zoneCookies.CollectionChanged -= ZoneCookies_CollectionChanged;
                _zoneCookies = value;
                if (_zoneCookies != null)
                    _zoneCookies.CollectionChanged += ZoneCookies_CollectionChanged;
                OnPropertyChanged(nameof(ZoneCookies));
                OnPropertyChanged(nameof(CookiesCount));
            }
        }

        private ObservableCollection<F95ZoneCookie> _zoneCookies = new ObservableCollection<F95ZoneCookie>();

        private void ZoneCookies_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(CookiesCount));
        }

        public PlayniteProperty LabelProperty { get; set; } = PlayniteProperty.Features;
        public PlayniteProperty TagProperty { get; set; } = PlayniteProperty.Tags;

        public int CookiesCount => ZoneCookies.Count;

        public bool CheckForUpdates { get; set; }
        public bool ShouldScrapeLinks { get; set; } = true;

        public bool UpdateFinishedGames { get; set; }

        public bool SetDefaultIcon { get; set; } = true;

        public CookieContainer? CreateCookieContainer()
        {
            var container = new CookieContainer();

            foreach (var cookie in ZoneCookies)
            {
                if (cookie.Name is null || cookie.Value is null) continue;

                container.Add(new Cookie(cookie.Name, cookie.Value, "/", "f95zone.to")
                {
                    Secure = true,
                    HttpOnly = true,
                    Expires = DateTime.Now + TimeSpan.FromDays(7)
                });
            }

            return container;

        }

        public Settings() 
        {
            ZoneCookies.CollectionChanged += (s, e) => OnPropertyChanged(nameof(CookiesCount));
        }

        public Settings(Plugin plugin, IPlayniteAPI playniteAPI)
        {
            _plugin = plugin;
            _playniteAPI = playniteAPI;

            var savedSettings = plugin.LoadPluginSettings<Settings>();
            if (savedSettings is not null)
            {
                ZoneCookies = savedSettings.ZoneCookies;
                LabelProperty = savedSettings.LabelProperty;
                TagProperty = savedSettings.TagProperty;
                CheckForUpdates = savedSettings.CheckForUpdates;
                ShouldScrapeLinks = savedSettings.ShouldScrapeLinks;
                UpdateFinishedGames = savedSettings.UpdateFinishedGames;
                SetDefaultIcon = savedSettings.SetDefaultIcon;
            }
        }

        public void DoLogin()
        {
            if (_playniteAPI is null) throw new InvalidDataException();

            var webView = _playniteAPI.WebViews.CreateView(new WebViewSettings
            {
                UserAgent = "Playnite.Extensions",
                JavaScriptEnabled = true,
                WindowWidth = 900,
                WindowHeight = 700
            });

            webView.Open();
            webView.Navigate(LoginUrl);

            webView.LoadingChanged += WebViewOnLoadingChanged;
        }

        private void AddCookieToList(HttpCookie cookie)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                this.ZoneCookies.Add(new F95ZoneCookie(cookie.Name, cookie.Value));
            });
        }

        private async void WebViewOnLoadingChanged(object sender, WebViewLoadingChangedEventArgs args)
        {
            List<string> whitelistedCookies = new List<string>
            {
                "xf_user",
                "xf_csrf",
                "xf_session",
                "__ddg1_",
                "__ddg2_",
                "__ddg3_",
                "__ddg4_",
                "__ddg5_",
                "__ddg6_",
                "__ddg7_",
                "__ddg8_",
                "__ddg9_",
                "__ddg10_",
                "__ddgid_",
                "__ddgmark_",
                "ddg_last_challenge",
            };

            if (args.IsLoading) return;
            if (sender is not IWebView web) throw new NotImplementedException();

            var address = web.GetCurrentAddress();
            if (address is null || address.StartsWith(LoginUrl)) return;

            await Task.Run(() =>
            {
                var cookies = web.GetCookies();
                if (cookies is null || !cookies.Any()) return;

                Application.Current.Dispatcher.Invoke(() =>
                {
                    this.ZoneCookies.Clear();
                });

                // Extract cookies from the web view
                foreach (var cookie in cookies)
                {
                    if (cookie.Name is null || cookie.Value is null) continue;

                    if (whitelistedCookies.Any(x => x == cookie.Name))
                    {
                        AddCookieToList(cookie);
                    }
                }

                _plugin?.SavePluginSettings(this);

                web.Close();
            });
        }


        private static string? GetCookie(IEnumerable<HttpCookie> cookies, string name)
        {
            var cookie = cookies.FirstOrDefault(x => x.Name is not null && x.Value is not null && x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            return cookie?.Value;
        }

        private Settings? _previousSettings;

        public void BeginEdit()
        {
            _previousSettings = new Settings
            {
                ZoneCookies = ZoneCookies,
                LabelProperty = LabelProperty,
                TagProperty = TagProperty,
                CheckForUpdates = CheckForUpdates,
                ShouldScrapeLinks = ShouldScrapeLinks,
                UpdateFinishedGames = UpdateFinishedGames,
                SetDefaultIcon = SetDefaultIcon
            };
        }

        public void EndEdit()
        {
            _previousSettings = null;
            _plugin?.SavePluginSettings(this);
        }

        public void CancelEdit()
        {
            if (_previousSettings is null) return;

            ZoneCookies = _previousSettings.ZoneCookies;
            LabelProperty = _previousSettings.LabelProperty;
            TagProperty = _previousSettings.TagProperty;
            CheckForUpdates = _previousSettings.CheckForUpdates;
            ShouldScrapeLinks = _previousSettings.ShouldScrapeLinks;
            UpdateFinishedGames = _previousSettings.UpdateFinishedGames;
            SetDefaultIcon = _previousSettings.SetDefaultIcon;
        }

        public bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();

            if (!ZoneCookies.Any(x => x.Name == "xf_user"))
            {
                errors.Add("The xf_user cookie has to be set!");
            }

            if (!ZoneCookies.Any(x => x.Name == "xf_csrf"))
            {
                errors.Add("The xf_csrf cookie has to be set!");
            }

            if (!Enum.IsDefined(typeof(PlayniteProperty), LabelProperty))
            {
                errors.Add($"Unknown value \"{LabelProperty}\"");
            }

            if (!Enum.IsDefined(typeof(PlayniteProperty), TagProperty))
            {
                errors.Add($"Unknown value \"{TagProperty}\"");
            }

            return !errors.Any();
        }
    }
}
