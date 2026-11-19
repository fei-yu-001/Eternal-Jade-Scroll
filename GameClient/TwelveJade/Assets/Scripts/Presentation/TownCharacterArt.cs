using System;
using System.Collections.Generic;
using UnityEngine;
namespace TwelveJade.Presentation
{
    public static class TownCharacterArt
    {
        [Serializable] public sealed class Entry
        {
            public string id="", stand="", walk="", nativeFacing="right"; public int frames=4; public float cycleDistance=240;
        }
        [Serializable] sealed class Table { public Entry[] entries=Array.Empty<Entry>(); }
        static Dictionary<string,Entry> entries;
        static readonly Dictionary<string,Texture2D> cache=new Dictionary<string,Texture2D>();
        public static Entry Find(string id)
        {
            if(entries==null)
            {
                entries=new Dictionary<string,Entry>();var asset=Resources.Load<TextAsset>("Config/town-character-art");
                var table=asset==null?null:JsonUtility.FromJson<Table>(asset.text);
                if(table?.entries!=null)foreach(var entry in table.entries)entries[entry.id]=entry;
            }
            return id!=null&&entries.TryGetValue(id,out var value)?value:null;
        }
        public static Texture2D Texture(string path)
        {
            if(string.IsNullOrEmpty(path))return null;
            if(!cache.TryGetValue(path,out var texture)){texture=Resources.Load<Texture2D>(path);cache[path]=texture;}return texture;
        }
        public static Texture2D Hero(string gender,string view,bool walk)
        {
            var entry=Find("hero-"+(gender=="female"?"female":"male")+"-"+view);
            return entry==null?null:Texture(walk?entry.walk:entry.stand);
        }
    }
}
