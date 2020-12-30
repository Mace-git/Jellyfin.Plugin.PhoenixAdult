using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using PhoenixAdult.Configuration;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

namespace PhoenixAdult.Sites
{
    public class NetworkJAVLibrary : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(searchTitle))
            {
                return result;
            }

            string searchJAVID = null;
            var splitedTitle = searchTitle.Split();
            if (splitedTitle.Length > 1 && int.TryParse(splitedTitle[1], out _))
            {
                searchJAVID = $"{splitedTitle[0]}-{splitedTitle[1]}";
            }

            if (!string.IsNullOrEmpty(searchJAVID))
            {
                searchTitle = searchJAVID;
            }

            for (var i = 0; i < Database.SiteList.Sites[siteNum[0]].Count; i++)
            {
                siteNum[1] = i;
                var url = Helper.GetSearchSearchURL(siteNum) + searchTitle;
                var data = await HTML.ElementFromURL(url, cancellationToken).ConfigureAwait(false);

                var searchResults = data.SelectNodesSafe("//div[@class='videos']//div[@class='video']");
                if (searchResults.Any())
                {
                    foreach (var searchResult in searchResults)
                    {
                        string sceneURL = $"{Helper.GetSearchBaseURL(siteNum)}/en/?v={searchResult.SelectSingleText(".//a/@id")}",
                            curID = Helper.Encode(sceneURL),
                            sceneName = searchResult.SelectSingleText(".//div[@class='title']"),
                            scenePoster = $"http:{searchResult.SelectSingleText(".//img/@src").Replace("ps.", "pl.", StringComparison.OrdinalIgnoreCase)}",
                            javID = searchResult.SelectSingleText(".//div[@class='id']");

                        var res = new RemoteSearchResult
                        {
                            ProviderIds = { { Plugin.Instance.Name, curID } },
                            Name = $"{javID} {sceneName}",
                            ImageUrl = scenePoster,
                        };

                        if (!string.IsNullOrEmpty(searchJAVID))
                        {
                            res.IndexNumber = 100 - LevenshteinDistance.Calculate(searchJAVID, javID, StringComparison.OrdinalIgnoreCase);
                        }

                        result.Add(res);
                    }
                }
                else
                {
                    var sceneURL = Helper.GetSearchBaseURL(siteNum) + data.SelectSingleText("//div[@id='video_title']//a/@href");
                    var sceneID = new string[] { Helper.Encode(sceneURL) };

                    var searchResult = await Helper.GetSearchResultsFromUpdate(this, siteNum, sceneID, searchDate, cancellationToken).ConfigureAwait(false);
                    if (searchResult.Any())
                    {
                        result.AddRange(searchResult);
                    }
                }

                if (result.Any())
                {
                    break;
                }
            }

            return result;
        }

        public async Task<MetadataResult<Movie>> Update(int[] siteNum, string[] sceneID, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Movie>()
            {
                Item = new Movie(),
                People = new List<PersonInfo>(),
            };

            if (sceneID == null)
            {
                return result;
            }

            var sceneURL = Helper.Decode(sceneID[0]);
            var sceneData = await HTML.ElementFromURL(sceneURL, cancellationToken).ConfigureAwait(false);

            result.Item.ExternalId = sceneURL;

            var javID = sceneData.SelectSingleText("//div[@id='video_id']//td[@class='text']");

            result.Item.OriginalTitle = javID.ToUpperInvariant();
            var title = sceneData.SelectSingleText("//div[@id='video_title']//h3");
            if (!string.IsNullOrEmpty(javID))
            {
                title = title.Replace(javID, string.Empty, StringComparison.OrdinalIgnoreCase);
            }

            result.Item.Name = title;

            var studio = sceneData.SelectSingleText("//div[@id='video_maker']//td[@class='text']");
            if (!string.IsNullOrEmpty(studio))
            {
                result.Item.AddStudio(studio);
            }

            var date = sceneData.SelectSingleText("//div[@id='video_date']//td[@class='text']");
            if (DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var sceneDateObj))
            {
                result.Item.PremiereDate = sceneDateObj;
            }

            var genreNode = sceneData.SelectNodesSafe("//div[@id='video_genres']//td[@class='text']//a");
            foreach (var genreLink in genreNode)
            {
                var genreName = genreLink.InnerText;

                result.Item.AddGenre(genreName);
            }

            var actorsNode = sceneData.SelectNodesSafe("//div[@id='video_cast']//td[@class='text']//span[@class='cast']//a");
            foreach (var actorLink in actorsNode)
            {
                var actorName = actorLink.InnerText;

                if (actorName != "----")
                {
                    switch (Plugin.Instance.Configuration.JAVActorNamingStyle)
                    {
                        case JAVActorNamingStyle.WesternStyle:
                            actorName = string.Join(" ", actorName.Split().Reverse());
                            break;
                    }

                    var actor = new PersonInfo
                    {
                        Name = actorName,
                    };

                    result.People.Add(actor);
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var result = new List<RemoteImageInfo>();

            if (sceneID == null)
            {
                return result;
            }

            var sceneURL = Helper.Decode(sceneID[0]);
            var sceneData = await HTML.ElementFromURL(sceneURL, cancellationToken).ConfigureAwait(false);

            var img = sceneData.SelectSingleText("//img[@id='video_jacket_img']/@src");
            if (!string.IsNullOrEmpty(img))
            {
                result.Add(new RemoteImageInfo
                {
                    Url = $"http:{img}",
                    Type = ImageType.Primary,
                });
            }

            var sceneImages = sceneData.SelectNodesSafe("//div[@class='previewthumbs']/img");
            foreach (var sceneImage in sceneImages)
            {
                img = $"http:{sceneImage.Attributes["src"].Value.Replace("-", "jp-", StringComparison.OrdinalIgnoreCase)}";

                result.Add(new RemoteImageInfo
                {
                    Url = img,
                    Type = ImageType.Primary,
                });

                result.Add(new RemoteImageInfo
                {
                    Url = img,
                    Type = ImageType.Backdrop,
                });
            }

            return result;
        }
    }
}
