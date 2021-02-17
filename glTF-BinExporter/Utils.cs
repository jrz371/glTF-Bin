using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Render;

namespace glTF_BinExporter
{
    public static class Utils
    {
        public static int AddAndReturnIndex<T>(this List<T> list, T item)
        {
            list.Add(item);
            return list.Count - 1;
        }

        public static bool IsFileGltfBinary(string filename)
        {
            string extension = Path.GetExtension(filename);

            return extension.ToLower() == ".glb";
        }
    }
}
