using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace kanimal_cli
{
    public struct ConversionData
    {
        public string filePath, animPath, buildPath, outputPath;

        public ConversionData(string filePath, string animPath, string buildPath, string outputPath)
        {
            this.filePath = filePath;
            this.animPath = animPath;
            this.buildPath = buildPath;
            this.outputPath = outputPath;
        }
    }
}
