using Newtonsoft.Json;

using System.Text;

namespace StepFlow.DataModel.Entities.DataSource
{
    /// <summary>
    /// Extension methods for configuration classes
    /// </summary>
    public static class IConfigurationExtensions
    {
        /// <summary>
        /// Serializes the configuration
        /// </summary>
        /// <typeparam name="T">The configuration object to serialise. Should inherit from <see cref="IConfigurationModel"/></typeparam>
        /// <param name="configurationModel">the model to serialise</param>
        /// <returns>the serialized object in JSON string format</returns>
        public static string Serialise<T>(this IConfigurationModel configurationModel)
           where T : class
        {
            var settings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore,
                MissingMemberHandling = MissingMemberHandling.Ignore
            };
            return JsonConvert.SerializeObject(configurationModel, settings);
        }

        /// <summary>
        /// Deserialises a JSON string to the desired object
        /// </summary>
        /// <typeparam name="T">the target object</typeparam>
        /// <param name="json">the json string to deserialise</param>
        /// <returns>the serialised object</returns>
        public static T Deserialise<T>(this string json)
        {
            var settings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore,
                MissingMemberHandling = MissingMemberHandling.Ignore
            };

            return JsonConvert.DeserializeObject<T>(ConvertToUtf8(json), settings);
        }

        /// <summary>
        /// Writes the JSON string to a file
        /// </summary>
        /// <param name="json">the JSON string to write</param>
        /// <param name="fileName">the target file</param>
        /// <returns>a <see cref="FileInfo"/> describing the output file</returns>
        public static FileInfo SaveAs(this string json, string fileName)
        {
            using (var output = new StreamWriter(fileName, false))
            {
                output.Write(json);
                return new FileInfo(fileName);
            }
        }

        /// <summary>
        /// Loads the contents of a file to a string object
        /// </summary>
        /// <param name="jsonFileInfo">the target string object where the contents of the file will be copied to</param>
        /// <returns>The content of the JSON file</returns>
        public static string Load(this FileInfo jsonFileInfo)
        {
            using (var input = new StreamReader(jsonFileInfo.FullName))
            {
                return ConvertToUtf8(input.ReadToEnd());
            }
        }

        /// <summary>
        /// Ensure we convert our Text to UTF8
        /// </summary>
        /// <param name="textOriginal">The original text</param>
        /// <returns>converted to UTF8 text</returns>
        private static string ConvertToUtf8(string textOriginal)
        {
            if (!string.IsNullOrEmpty(textOriginal))
            {
                byte[] bytes = Encoding.Default.GetBytes(textOriginal);
                return Encoding.UTF8.GetString(bytes);
            }

            return string.Empty;
        }
    }
}
