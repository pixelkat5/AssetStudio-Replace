using System.IO;

namespace AssetStudio
{
    public class ResourceReader
    {
        private bool needSearch;
        private string path;
        private SerializedFile assetsFile;
        private long size;
        private BinaryReader reader;

        public int Size
        {
            get => (int)size;
            set => size = value;
        }
        public long Offset { get; set; }

        public ResourceReader() { }

        public ResourceReader(string path, SerializedFile assetsFile, long offset, long size)
        {
            needSearch = true;
            this.path = path;
            this.assetsFile = assetsFile;
            this.Offset = offset;
            this.size = size;
        }

        public ResourceReader(BinaryReader reader, long offset, long size)
        {
            this.reader = reader;
            this.Offset = offset;
            this.size = size;
        }

        private BinaryReader GetReader()
        {
            if (needSearch)
            {
                var resourceFileName = Path.GetFileName(path);
                if (assetsFile.assetsManager.resourceFileReaders.TryGetValue(resourceFileName, out reader))
                {
                    needSearch = false;
                    return reader;
                }
                var assetsFileDirectory = Path.GetDirectoryName(assetsFile.fullName);
                var resourceFilePath = Path.Combine(assetsFileDirectory, resourceFileName);
                if (!File.Exists(resourceFilePath))
                {
                    var findFiles = Directory.GetFiles(assetsFileDirectory, resourceFileName, SearchOption.AllDirectories);
                    if (findFiles.Length > 0)
                    {
                        resourceFilePath = findFiles[0];
                    }
                }
                if (File.Exists(resourceFilePath))
                {
                    needSearch = false;
                    if (assetsFile.assetsManager.resourceFileReaders.TryGetValue(resourceFileName, out reader))
                    {
                        return reader;
                    }
                    reader = new BinaryReader(File.OpenRead(resourceFilePath));
                    assetsFile.assetsManager.resourceFileReaders.TryAdd(resourceFileName, reader);
                    return reader;
                }
                throw new FileNotFoundException($"Can't find the resource file {resourceFileName}");
            }
            else
            {
                return reader;
            }
        }

        public byte[] GetData()
        {
            var binaryReader = GetReader();
            lock (binaryReader)
            {
                binaryReader.BaseStream.Position = Offset;
                return binaryReader.ReadBytes((int)size);
            }
        }

        public int GetData(byte[] buff, int startIndex = 0)
        {
            int dataLen;
            var binaryReader = GetReader();
            lock (binaryReader)
            {
                binaryReader.BaseStream.Position = Offset;
                dataLen = binaryReader.Read(buff, startIndex, (int)size);
            }
            return dataLen;
        }

        public void WriteData(string path)
        {
            var binaryReader = GetReader();
            binaryReader.BaseStream.Position = Offset;
            using (var writer = File.OpenWrite(path))
            {
                binaryReader.BaseStream.CopyTo(writer, size);
            }
        }

        /// <summary>
        /// Returns the on-disk path backing this resource's data, or null if the data doesn't
        /// live in a real file we can write back to (e.g. it was decompressed into memory from
        /// a compressed asset bundle). Used to determine whether in-place replacement is possible.
        /// </summary>
        public string GetPatchableFilePath()
        {
            var binaryReader = GetReader();
            return (binaryReader.BaseStream as FileStream)?.Name;
        }

        /// <summary>
        /// Overwrites this resource's bytes directly in the backing file on disk.
        /// Only works when <see cref="GetPatchableFilePath"/> returns a real path and
        /// <paramref name="newData"/> is exactly <see cref="Size"/> bytes long (the data can't
        /// grow or shrink in place without rewriting the whole container file).
        /// </summary>
        public bool TryPatchData(byte[] newData)
        {
            if (newData == null || newData.Length != size)
            {
                return false;
            }

            var filePath = GetPatchableFilePath();
            if (filePath == null)
            {
                return false;
            }

            using (var writeStream = new FileStream(filePath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
            {
                writeStream.Position = Offset;
                writeStream.Write(newData, 0, newData.Length);
                writeStream.Flush();
            }

            return true;
        }
    }
}
