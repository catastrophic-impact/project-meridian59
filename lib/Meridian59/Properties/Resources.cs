using System.IO;
using System.Reflection;

namespace Meridian59.Properties
{
    /// <summary>Replacement for the WinForms resx designer: loads colortable.hex from an embedded resource.</summary>
    internal static class Resources
    {
        private static byte[] colorTable;
        internal static byte[] BitmapColorTable
        {
            get
            {
                if (colorTable == null)
                {
                    using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("colortable.hex"))
                    using (MemoryStream ms = new MemoryStream())
                    {
                        s.CopyTo(ms);
                        colorTable = ms.ToArray();
                    }
                }
                return colorTable;
            }
        }
    }
}
