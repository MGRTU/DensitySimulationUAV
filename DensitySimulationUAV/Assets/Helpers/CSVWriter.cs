using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Assets.Helpers
{
    /// <summary>
    /// Class that takes care of CSV writing operations
    /// </summary>
    public class CSVWriter : MonoBehaviour
    {
        public static CSVWriter Instance;

        public string DirectoryPath;
        public string FileName;

        public string Separator;

        // Start is called before the first frame update
        private void Start()
        {
            Instance = this;
        }

        /// <summary>
        /// Write List of string arrays to a CSV file.
        /// The first item in the list has to be the column names
        /// </summary>
        /// <param name="dataList">List of lines of CSV file as a string array</param>
        /// <param name="fileName">File name in the directory path</param>
        public void WriteToCsv(List<string[]> dataList, string fileName)
        {
            var filePath = Path.Combine(DirectoryPath, fileName);

            using var writer = new StreamWriter(filePath);
            foreach (var row in dataList.Select(dataRow => string.Join(Separator.ToString(), dataRow)))
            {
                writer.WriteLine(row);
            }
        }
    }
}
