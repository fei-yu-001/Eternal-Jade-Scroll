using System;
using System.Collections.Generic;
using UnityEngine;
namespace TwelveJade.Presentation
{
    // Runtime lookup and the asset gate share exactly this declaration; texture lifetimes stay with callers.
    public static class ApprovedUiArt
    {
        [Serializable] sealed class Entry { public string id="",resource=""; }
        [Serializable] sealed class Catalogue { public Entry[] artworks=Array.Empty<Entry>(); }
        static Dictionary<string,string> paths;
        static void EnsurePaths()
        {
            if(paths!=null)return;
            paths=new Dictionary<string,string>();var asset=Resources.Load<TextAsset>("Config/ui-art-catalogue");
            var catalogue=asset==null?null:JsonUtility.FromJson<Catalogue>(asset.text);
            if(catalogue?.artworks!=null)foreach(var row in catalogue.artworks)paths[row.id]=row.resource;
        }
        public static bool Has(string id)
        {
            EnsurePaths();
            return paths.ContainsKey(id);
        }
        public static Texture2D Load(string id)
        {
            EnsurePaths();
            if(!paths.TryGetValue(id,out var path)){Debug.LogError("Undeclared UI art: "+id);return null;}
            return Resources.Load<Texture2D>(path);
        }
    }
}
