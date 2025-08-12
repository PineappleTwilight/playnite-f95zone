using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace F95ZoneMetadataProvider
{
    public enum PlayniteProperty
    {
        Features = 0,
        Genres = 1,
        Tags = 2
    }

    public static class PlaynitePropertyHelper
    {
        /// <summary>
        /// Retrieves a collection of <see cref="DatabaseObject"/> items from the Playnite database
        /// based on the specified <paramref name="property"/>.
        /// </summary>
        /// <param name="playniteAPI">The <see cref="IPlayniteAPI"/> instance to access the Playnite database.</param>
        /// <param name="property">A <see cref="PlayniteProperty"/> value indicating which collection to return.</param>
        /// <returns>
        /// An <see cref="IEnumerable{DatabaseObject}"/> containing the requested collection of database objects,
        /// or <c>null</c> if the specified <paramref name="property"/> does not match a known collection.
        /// </returns>
        public static IEnumerable<DatabaseObject>? GetDatabaseCollection(IPlayniteAPI playniteAPI, PlayniteProperty property)
        {
            return property switch
            {
                PlayniteProperty.Features => playniteAPI.Database.Features,
                PlayniteProperty.Genres => playniteAPI.Database.Genres,
                PlayniteProperty.Tags => playniteAPI.Database.Tags,
                _ => null
            };
        }

        /// <summary>
        /// Converts a sequence of string values into <see cref="MetadataProperty"/> instances.
        /// For each value, attempts to find a matching property in the database collection:
        /// if found, returns a <see cref="MetadataIdProperty"/> using the property ID;
        /// otherwise, returns a <see cref="MetadataNameProperty"/> using the original value.
        /// </summary>
        /// <param name="playniteAPI">The Playnite API instance for accessing the database.</param>
        /// <param name="values">The collection of string values to convert.</param>
        /// <param name="currentProperty">The current property definition used to select the database collection.</param>
        /// <returns>
        /// A list of <see cref="MetadataProperty"/> instances corresponding to the input values,
        /// or <c>null</c> if the database collection could not be retrieved.
        /// </returns>
        public static IEnumerable<MetadataProperty>? ConvertValuesToProperties(IPlayniteAPI playniteAPI, IEnumerable<string> values, PlayniteProperty currentProperty)
        {
            var collection = GetDatabaseCollection(playniteAPI, currentProperty);
            if (collection is null) return null;

            var metadataProperties = values
                .Select(value => (value, collection.Where(x => x.Name is not null).FirstOrDefault(x => x.Name.Equals(value, StringComparison.OrdinalIgnoreCase))))
                .Select(tuple =>
                {
                    var (value, property) = tuple;
                    if (property is not null) return (MetadataProperty)new MetadataIdProperty(property.Id);
                    return new MetadataNameProperty(value);
                })
                .ToList();

            return metadataProperties;
        }

        /// <summary>
        /// Attempts to convert values provided by <paramref name="getValues"/> into a collection of <see cref="MetadataProperty"/>
        /// if <paramref name="settingsProperty"/> matches <paramref name="currentProperty"/>.
        /// </summary>
        /// <param name="playniteAPI">The Playnite API instance used for conversion.</param>
        /// <param name="settingsProperty">The property whose values are to be converted.</param>
        /// <param name="currentProperty">The property currently being processed.</param>
        /// <param name="getValues">A function that retrieves the values to convert.</param>
        /// <returns>
        /// A collection of <see cref="MetadataProperty"/> if conversion is possible; otherwise <c>null</c>.
        /// </returns>
        public static IEnumerable<MetadataProperty>? ConvertValuesIfPossible(
            IPlayniteAPI playniteAPI,
            PlayniteProperty settingsProperty,
            PlayniteProperty currentProperty,
            Func<IEnumerable<string>?> getValues)
        {
            if (settingsProperty != currentProperty) return null;

            var values = getValues();
            if (values is null) return null;

            var properties = ConvertValuesToProperties(playniteAPI, values, settingsProperty);
            return properties ?? null;
        }

        /// <summary>
        /// Concatenates multiple <see cref="MetadataProperty"/> sequences, skipping null sequences.
        /// </summary>
        /// <param name="enumerables">
        /// An array of <see cref="IEnumerable{MetadataProperty}"/> instances, any of which may be null.
        /// </param>
        /// <returns>
        /// A single <see cref="IEnumerable{MetadataProperty}"/> that is the concatenation of all non-null inputs,
        /// or null if all provided sequences are null.
        /// </returns>
        /// <remarks>
        /// To minimize allocations, this method locates the first non-null enumerable and uses it as the starting
        /// sequence, then concatenates subsequent non-null sequences onto it instead of building a new collection.
        /// </remarks>
        public static IEnumerable<MetadataProperty>? MultiConcat(params IEnumerable<MetadataProperty>?[] enumerables)
        {
            /*
             * To reduce allocations we look for the first enumerable that is not null and use that as the starting point.
             * This is more memory efficient than starting with an empty enumerable and appending everything to that.
             */

            var start = -1;
            for (var i = 0; i < enumerables.Length; i++)
            {
                if (enumerables[i] is null) continue;

                start = i;
                break;
            }

            if (start == -1) return null;
            var res = enumerables[start++]!;

            for (var i = start; i < enumerables.Length; i++)
            {
                var cur = enumerables[i];
                if (cur is null) continue;

                res = res.Concat(cur);
            }

            return res;
        }
    }
}