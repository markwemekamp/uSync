﻿using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using Umbraco.Core;
using Umbraco.Core.Configuration;
using Umbraco.Core.IO;
using Umbraco.Core.Logging;
using Umbraco.Core.Services;

namespace Jumoo.uSync.Core.Mappers
{
    public class GridMapper : IContentMapper
    {
        private IDataTypeService _dataTypeService;

        public GridMapper()
        {
            _dataTypeService = ApplicationContext.Current.Services.DataTypeService;
        }

        public string GetExportValue(int dataTypeDefinitionId, string value)
        {
            return ProcessGridValues(value, false); // export 

        }

        public string GetImportValue(int dataTypeDefinitionId, string content)
        {
            return ProcessGridValues(content, true); // import 
        }


        /// <summary>
        ///  Grid imports and exports are near identical, but they do involve a lot 
        ///  of walking through the Grid JSON, so this function does all that and 
        ///  then just calls the import or export of the grid value depending
        ///  on what type it is (in the usync.config file)
        /// </summary>
        /// <param name="content"></param>
        /// <param name="import"></param>
        /// <returns></returns>
        private string ProcessGridValues(string content, bool import)
        {
            var usyncMappings = uSyncCoreContext.Instance.Configuration.Settings.ContentMappings;

            var grid = JsonConvert.DeserializeObject<JObject>(content);
            if (grid == null)
                return content;

            var gridConfig = UmbracoConfig.For.GridConfig(
                ApplicationContext.Current.ProfilingLogger.Logger,
                ApplicationContext.Current.ApplicationCache.RuntimeCache,
                new DirectoryInfo(HttpContext.Current.Server.MapPath(SystemDirectories.AppPlugins)),
                new DirectoryInfo(HttpContext.Current.Server.MapPath(SystemDirectories.Config)),
                HttpContext.Current.IsDebuggingEnabled);

            var sections = GetArray(grid, "sections");
            foreach (var section in sections.Cast<JObject>())
            {
                var rows = GetArray(section, "rows");
                foreach (var row in rows.Cast<JObject>())
                {
                    var areas = GetArray(row, "areas");
                    foreach (var area in areas.Cast<JObject>())
                    {
                        var controls = GetArray(area, "controls");
                        foreach (var control in controls.Cast<JObject>())
                        {
                            var editor = control.Value<JObject>("editor");
                            if (editor != null)
                            {
                                var alias = editor.Value<string>("alias");
                                if (alias.IsNullOrWhiteSpace() == false)
                                {

                                    var grid_alias = string.Format("grid.{0}", alias);
                                    var mapping = usyncMappings.SingleOrDefault(x => x.EditorAlias == grid_alias);
                                    if (mapping == null)
                                    {
                                        // leblender style lookup, look for the name (from the config)
                                        var config = gridConfig.EditorsConfig.Editors.FirstOrDefault(x => x.Alias == alias);
                                        if (config != null)
                                        {
                                            LogHelper.Debug<GridMapper>("Looking for view: {0}", () => config.View);

                                            if (!string.IsNullOrEmpty(config.View))
                                                mapping = usyncMappings.SingleOrDefault(x => !string.IsNullOrEmpty(x.View) && config.View.IndexOf(x.View, StringComparison.InvariantCultureIgnoreCase)>0);
                                        }
                                    }

                                    if (mapping != null)
                                    {
                                        LogHelper.Debug<GridMapper>("Mapping: {0}", () => mapping.EditorAlias);
                                        var propertyName = mapping.Settings;

                                        if (propertyName != null)
                                        {
                                            var mapper = ContentMapperFactory.GetMapper(mapping);
                                            if (mapper != null)
                                            {
                                                var propValue = control.Value<object>(propertyName);
                                                var mappedValue = "";
                                                if (import)
                                                    mappedValue = mapper.GetImportValue(0, propValue.ToString());
                                                else
                                                    mappedValue = mapper.GetExportValue(0, propValue.ToString());

                                                if (!IsJson(mappedValue))
                                                    control[propertyName] = mappedValue;
                                                else
                                                {
                                                    var mappedJson = JToken.Parse(mappedValue);
                                                    if (mappedJson != null)
                                                    {
                                                        control[propertyName] = mappedJson;
                                                    }
                                                }

                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            return JsonConvert.SerializeObject(grid, Formatting.Indented);

        }

        private bool IsJson(string val)
        {
            val = val.Trim();
            return (val.StartsWith("{") && val.EndsWith("}"))
                || (val.StartsWith("[") && val.EndsWith("]"));
        }

        private JArray GetArray(JObject obj, string propertyName)
        {
            JToken token;
            if (obj.TryGetValue(propertyName, out token))
            {
                var asArray = token as JArray;
                return asArray ?? new JArray();
            }
            return new JArray();
        }
    }
}
