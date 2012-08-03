﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Lucene.Net.Analysis;
using Lucene.Net.Index;
using Sitecore.Configuration;
using Sitecore.Diagnostics;
using Sitecore.IO;
using Sitecore.ItemBucket.Kernel.Util;
using Sitecore.ItemBuckets.BigData.RemoteIndex;
using Sitecore.Search;
using Sitecore.Search.Crawlers;
using Directory = Lucene.Net.Store.Directory;
using FSDirectory = Lucene.Net.Store.FSDirectory;
using IndexSearcher = Lucene.Net.Search.IndexSearcher;

namespace Sitecore.ItemBuckets.BigData.RemoteIndex
{
    public class RemoteIndex : ILuceneIndex
    {
        private Analyzer _analyzer;
        private readonly List<IRemoteCrawler> _crawlers = new List<IRemoteCrawler>();
        private readonly FSDirectory _directory;
        private readonly string _folder;
        private readonly string _name;

        // Methods
        public RemoteIndex(string name, string folder)
        {
            this._name = name;
            this._folder = folder;
            this._directory = this.CreateDirectory(FileUtil.MapPath(FileUtil.MakePath(Config.RemoteIndexingServer, folder)));
        }

        public void AddCrawler(IRemoteCrawler crawler)
        {
            crawler.Initialize(this);
            this._crawlers.Add(crawler);
        }

        public IndexDeleteContext CreateDeleteContext()
        {
            return new IndexDeleteContext(this);
        }

        protected virtual FSDirectory CreateDirectory(string folder)
        {
            FileUtil.EnsureFolder(folder);
            Lucene.Net.Store.FSDirectory directory = Lucene.Net.Store.FSDirectory.GetDirectory(folder, false);
            using (new IndexLocker(directory.MakeLock("write.lock")))
            {
                if (!IndexReader.IndexExists(directory))
                {
                    new IndexWriter(directory, this._analyzer, true).Close();
                }
            }
            return directory;
        }

        protected virtual IndexReader CreateReader()
        {
            return Assert.ResultNotNull<IndexReader>(IndexReader.Open(this._directory), "Could not create reader for index " + this.Name);
        }

        public IndexSearchContext CreateSearchContext()
        {
            return new IndexSearchContext(this);
        }

        protected virtual IndexSearcher CreateSearcher(bool close)
        {
            if (close)
            {
                return new IndexSearcher(this.Directory);
            }
            return new IndexSearcher(this.CreateReader());
        }

        public IndexUpdateContext CreateUpdateContext()
        {
            return new IndexUpdateContext(this);
        }

        protected virtual IndexWriter CreateWriter(bool recreate)
        {
            using (new IndexLocker(this._directory.MakeLock("write.lock")))
            {
                recreate |= !IndexReader.IndexExists(this._directory);
                return new IndexWriter(this._directory, this._analyzer, recreate);
            }
        }

        public int GetDocumentCount()
        {
            int num;
            IndexSearcher searcher = this.CreateSearcher(true);
            try
            {
                num = searcher.Reader.NumDocs();
            }
            finally
            {
                searcher.Close();
            }
            return num;
        }

        public void Rebuild()
        {
            using (IndexUpdateContext context = this.CreateUpdateContext())
            {
                foreach (IRemoteCrawler crawler in this._crawlers)
                {
                    crawler.Add(context);
                }
                context.Optimize();
                context.Commit();
            }

            File.Copy((Path.Combine(Config.RemoteIndexingServer, _folder)), Settings.IndexFolder);
        }

        private void Reset()
        {
            this.CreateWriter(true).Close();
        }

        IndexSearcher ILuceneIndex.CreateSearcher(bool close)
        {
            return this.CreateSearcher(close);
        }

        IndexWriter ILuceneIndex.CreateWriter(bool recreate)
        {
            return this.CreateWriter(recreate);
        }

        // Properties
        public Analyzer Analyzer
        {
            get
            {
                return this._analyzer;
            }
            set
            {
                this._analyzer = value;
            }
        }

        public Directory Directory
        {
            get
            {
                return this._directory;
            }
        }

        public string Name
        {
            get
            {
                return this._name;
            }
        }
    }
}
