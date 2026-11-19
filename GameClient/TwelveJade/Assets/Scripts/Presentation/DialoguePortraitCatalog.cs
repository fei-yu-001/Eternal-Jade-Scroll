using System;
using System.Collections.Generic;
using UnityEngine;
namespace TwelveJade.Presentation
{
    public static class DialoguePortraitCatalog
    {
        [Serializable] sealed class Entry { public string id = ""; public string resource = ""; }
        [Serializable] sealed class Table { public Entry[] portraits = Array.Empty<Entry>(); }
        static readonly Dictionary<string,Texture2D> textures = new Dictionary<string,Texture2D>();
        static Dictionary<string,string> paths;
        public static Texture2D Find(string id)
        {
            if(string.IsNullOrEmpty(id))return null;
            if(paths==null)
            {
                paths=new Dictionary<string,string>();var data=Resources.Load<TextAsset>("Config/dialogue-portraits");
                var table=data==null?null:JsonUtility.FromJson<Table>(data.text);
                if(table?.portraits!=null)foreach(var entry in table.portraits)
                    if(!string.IsNullOrEmpty(entry.id)&&!string.IsNullOrEmpty(entry.resource))paths[entry.id]=entry.resource;
            }
            if(textures.TryGetValue(id,out var texture))return texture;
            if(!paths.TryGetValue(id,out var path))return null;
            texture=Resources.Load<Texture2D>(path);textures[id]=texture;return texture;
        }
        public static Texture2D Hero(string gender) => Find(gender=="female"?"hero-female":"hero-male");
    }
}
