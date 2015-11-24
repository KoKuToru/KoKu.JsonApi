using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace KoKu.JsonApi {
    [AttributeUsage(AttributeTargets.Class)]
    public class Type : Attribute {
        public readonly string Value;
        
        public Type(string value) {
            this.Value = value;
        }
    }
    
    [AttributeUsage(AttributeTargets.Property)]
    public class Included : Attribute {}
    
    [AttributeUsage(AttributeTargets.Property)]
    public class Ignore : Attribute {}
    
    public class Serializer {
        private static List<object> fromObjectData(JsonWriter writer, object value) {
            var props = value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
            
            // get type
            writer.WritePropertyName("type");
            var type = ((Type)value.GetType().GetCustomAttribute(typeof(Type))).Value;
            writer.WriteValue(type);
                        
            // get id
            var id = value.GetType().GetProperty("Id");
            writer.WritePropertyName("id");
            writer.WriteValue(id.GetValue(value, null));
            
            var filteredProps = props.Where(x => x != id && !x.GetCustomAttributes(true).Any(y => y as Ignore != null));
            
            // get all attributes
            var relatedObjects = new List<PropertyInfo>();
            writer.WritePropertyName("attributes");
            writer.WriteStartObject();
            foreach (var prop in filteredProps) {                                      
                var propValue = prop.GetValue(value, null);
                if (propValue == null) {
                    continue;
                }
                if (propValue as String == null && 
                    propValue as IEnumerable != null) {
                    relatedObjects.Add(prop);
                    continue;
                }
                
                writer.WritePropertyName(prop.Name);
                writer.WriteValue(propValue);
            }
            writer.WriteEndObject();
            
            // get all relationships
            var includedObjects = new List<object>();
            if (relatedObjects.Count > 0) {
                writer.WritePropertyName("relationships");
                writer.WriteStartObject();
                foreach (var prop in relatedObjects) {
                    var propValue = prop.GetValue(value, null);
                    if (propValue == null) {
                        continue;
                    }
                    var propEnumerable = (IEnumerable)propValue;
                    bool included = prop.GetCustomAttributes(true).Any(x => x as Included != null);
                    
                    writer.WritePropertyName(prop.Name);
                    writer.WriteStartObject();
                    writer.WritePropertyName("data");
                    writer.WriteStartArray();
                    foreach (var item in propEnumerable) {
                        if (included) {
                            includedObjects.Add(item);
                        }
                        writer.WriteStartObject();
                        
                        // get type
                        writer.WritePropertyName("type");
                        var itemType = ((Type)item.GetType().GetCustomAttribute(typeof(Type))).Value;
                        writer.WriteValue(itemType);
                        
                        // get id
                        var itemId = item.GetType().GetProperty("Id");
                        writer.WritePropertyName("id");
                        writer.WriteValue(itemId.GetValue(item, null));
                        
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }
                writer.WriteEndObject();
            }
     
            return includedObjects;
        }
        
        public static string fromObject(List<object> value, bool deepInclude = false) {
            var includedObjects = new List<object>();            
            var includedObjectsBack = new List<object>();
            var sb = new StringBuilder();
            var sw = new StringWriter(sb);            
            
            using (JsonWriter writer = new JsonTextWriter(sw)) {
                writer.Formatting = Formatting.Indented;
                // TODO: add camelCase
                writer.WriteStartObject();
                
                // write data part
                writer.WritePropertyName("data");
                writer.WriteStartArray();
                foreach (var item in value) {
                    writer.WriteStartObject();
                    includedObjects.AddRange(fromObjectData(writer, item));
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                
                if (includedObjects.Count > 0) {
                    writer.WritePropertyName("included");
                    writer.WriteStartArray();
                    var currentIncludedObjects = includedObjects;
                    var nextIncludedObjects = includedObjectsBack;
                    do {
                        foreach (var item in currentIncludedObjects) {
                            writer.WriteStartObject();
                            if (deepInclude) {
                                nextIncludedObjects.AddRange(fromObjectData(writer, item));
                            } else {
                                fromObjectData(writer, item);
                            }
                            writer.WriteEndObject();
                        }
                        // swap references
                        var tmp = currentIncludedObjects;
                        currentIncludedObjects = nextIncludedObjects;
                        nextIncludedObjects = tmp;
                    } while (currentIncludedObjects.Count > 0);
                    
                    writer.WriteEndArray();
                }
                
                writer.WriteEndObject();
            }
            
            return sb.ToString();
        }
    }
    
    public class Deserializer {
        private static IEnumerable<System.Type> GetTypesWith<TAttribute>(bool inherit) 
                              where TAttribute: System.Attribute { 
             return from a in AppDomain.CurrentDomain.GetAssemblies()
                  from t in a.GetTypes()
                  where t.IsDefined(typeof(TAttribute), inherit)
                  select t;
        }
        
        private static object ConvertList(List<object> value, System.Type type) {
            var containedType = type.GenericTypeArguments.First();
            var list = (IList) Activator.CreateInstance(type);
            foreach (var v in value) {
                list.Add(Convert.ChangeType(v, containedType));
            }
            return list;
        }
        
        private static List<object> toObjectData(JToken token, JEnumerable<JObject> included) {
            var list = new List<object>();
            var ignoreCase = BindingFlags.IgnoreCase |  BindingFlags.Public | BindingFlags.Instance;
            
            // TODO: add deCamelCase
            var types = GetTypesWith<Type>(true);
            foreach (var data in token["data"].Children<JObject>()) {
                var itemType = (string)data["type"];
                var type = types.First(x => ((Type)x.GetCustomAttribute(typeof(Type))).Value == itemType);
                
                var item = Activator.CreateInstance(type);
                list.Add(item);

                // set id
                type.GetProperty("id", ignoreCase).SetValue(item, Convert.ChangeType(data["id"], type.GetProperty("id", ignoreCase).PropertyType), null);
                                
                var ndata = data;
                // there is probably a better way todo this
                if (included.Any(x => (string)x["type"] == (string)data["type"] && (string)x["id"] == (string)data["id"])) {
                    // get data from included
                    ndata = included.First(x => (string)x["type"] == (string)data["type"] && (string)x["id"] == (string)data["id"]);
                }
                
                // set attributes
                if (ndata["attributes"] as JObject != null) {
                    foreach (var attr in ((JObject)ndata["attributes"]).Properties()) {
                        type.GetProperty(attr.Name, ignoreCase).SetValue(item, Convert.ChangeType(attr.Value, type.GetProperty(attr.Name, ignoreCase).PropertyType), null);
                    }
                }
                
                // set relationships
                if (ndata["relationships"] as JObject != null) {
                    foreach (var rela in ((JObject)ndata["relationships"]).Properties()) {
                        // will this work ?
                        // just hopes it's a ?????<T> container
                        type.GetProperty(rela.Name, ignoreCase).SetValue(item, ConvertList(toObjectData(rela.Value, included), type.GetProperty(rela.Name, ignoreCase).PropertyType), null);
                    }
                }
            }
            
            return list;
        }
        
        public static List<object> toObject(string json) {
            JToken token = JObject.Parse(json);
            var included = token["included"].Children<JObject>();
           
            return toObjectData(token, included);
        }
    }
}
