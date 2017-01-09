﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using static PdfiumViewerHB.NativeMethods;

namespace PdfiumViewerHB
{
    internal abstract class PdfFile : IDisposable
    {
        private static readonly Encoding FPDFEncoding = new UnicodeEncoding(false, false, false);

        private IntPtr _document;
        private IntPtr _form;
        private bool _disposed;
        private NativeMethods.FPDF_FORMFILLINFO _formCallbacks;
        private GCHandle _formCallbacksHandle;

        public static PdfFile Create(Stream stream)
        {
            if (stream == null)
                throw new ArgumentNullException("stream");

            if (stream is MemoryStream)
                return new PdfMemoryStreamFile((MemoryStream)stream);
            if (stream is FileStream)
                return new PdfFileStreamFile((FileStream)stream);
            return new PdfBufferFile(StreamExtensions.ToByteArray(stream));
        }

        protected PdfFile()
        {
            PdfLibrary.EnsureLoaded();
        }

        public PdfBookmarkCollection Bookmarks { get; private set; }

        public bool RenderPDFPageToDC(int pageNumber, IntPtr dc, int dpiX, int dpiY, int boundsOriginX, int boundsOriginY, int boundsWidth, int boundsHeight, NativeMethods.FPDF flags)
        {
            if (_disposed)
                throw new ObjectDisposedException(GetType().Name);

            using (var pageData = new PageData(_document, _form, pageNumber))
            {
                NativeMethods.FPDF_RenderPage(dc, pageData.Page, boundsOriginX, boundsOriginY, boundsWidth, boundsHeight, 0, flags);
            }

            return true;
        }

        public byte[] GetPDFPage(int pageNumber)
        {
            if (_disposed)
                throw new ObjectDisposedException(GetType().Name);

            IntPtr newDocument = NativeMethods.FPDF_CreateNewDocument();

            var bl = NativeMethods.FPDF_ImportPages(newDocument, _document, pageNumber.ToString(), 0);

            int pageCount = NativeMethods.FPDF_GetPageCount(newDocument);


            using (MemoryStream ms = new MemoryStream())
            {

                FPDF_FILEWRITE saveData = new FPDF_FILEWRITE();
                saveData.WriteBlock = (WriteBlockCallback)((pThis, buffer, buflen) =>
                {
                    ms.Write(buffer, 0, buffer.Length);
                    return true;
                });
                try
                {
                    NativeMethods.FPDF_SaveAsCopy(newDocument, saveData, 1);

                }
                finally
                {
                    GC.KeepAlive((object)saveData);
                }

                NativeMethods.FPDF_CloseDocument(newDocument);

                return ms.ToArray();
            }

        }

        public void SetPageMediaBox(int index, float left, float right, float top, float bottom)
        {
            IntPtr pageHandle = FPDF_LoadPage(_document, index);
            FPDFPage_SetMediaBox(pageHandle, left, bottom, right, top);
            FPDF_ClosePage(pageHandle);
        }

        public PageRectangle GetPageMediaBox(int index)
        {
            var rect = new PageRectangle();
            IntPtr pageHandle = FPDF_LoadPage(_document, index);
            FPDFPage_GetMediaBox(pageHandle, out rect.left, out rect.bottom, out rect.right, out rect.top);
            FPDF_ClosePage(pageHandle);
            return rect;
        }


        public bool RenderPDFPageToBitmap(int pageNumber, IntPtr bitmapHandle, int dpiX, int dpiY, int boundsOriginX, int boundsOriginY, int boundsWidth, int boundsHeight, NativeMethods.FPDF flags)
        {
            if (_disposed)
                throw new ObjectDisposedException(GetType().Name);

            using (var pageData = new PageData(_document, _form, pageNumber))
            {
                NativeMethods.FPDF_RenderPageBitmap(bitmapHandle, pageData.Page, boundsOriginX, boundsOriginY, boundsWidth, boundsHeight, 0, flags);
            }

            return true;
        }

        public PdfPageLinks GetPageLinks(int pageNumber, Size pageSize)
        {
            if (_disposed)
                throw new ObjectDisposedException(GetType().Name);

            var links = new List<PdfPageLink>();

            using (var pageData = new PageData(_document, _form, pageNumber))
            {
                int link = 0;
                IntPtr annotation;

                while (NativeMethods.FPDFLink_Enumerate(pageData.Page, ref link, out annotation))
                {
                    var destination = NativeMethods.FPDFLink_GetDest(_document, annotation);
                    int? target = null;
                    string uri = null;

                    if (destination != IntPtr.Zero)
                        target = (int)NativeMethods.FPDFDest_GetPageIndex(_document, destination);

                    var action = NativeMethods.FPDFLink_GetAction(annotation);
                    if (action != IntPtr.Zero)
                    {
                        const uint length = 1024;
                        var sb = new StringBuilder(1024);
                        NativeMethods.FPDFAction_GetURIPath(_document, action, sb, length);

                        uri = sb.ToString();
                    }

                    var rect = new NativeMethods.FS_RECTF();

                    if (NativeMethods.FPDFLink_GetAnnotRect(annotation, rect) && (target.HasValue || uri != null))
                    {
                        int deviceX1;
                        int deviceY1;
                        int deviceX2;
                        int deviceY2;

                        NativeMethods.FPDF_PageToDevice(
                            pageData.Page,
                            0,
                            0,
                            pageSize.Width,
                            pageSize.Height,
                            0,
                            rect.left,
                            rect.top,
                            out deviceX1,
                            out deviceY1
                        );

                        NativeMethods.FPDF_PageToDevice(
                            pageData.Page,
                            0,
                            0,
                            pageSize.Width,
                            pageSize.Height,
                            0,
                            rect.right,
                            rect.bottom,
                            out deviceX2,
                            out deviceY2
                        );

                        links.Add(new PdfPageLink(
                            new Rectangle(deviceX1, deviceY1, deviceX2 - deviceX1, deviceY2 - deviceY1),
                            target,
                            uri
                        ));
                    }
                }
            }

            return new PdfPageLinks(links);
        }

        public List<SizeF> GetPDFDocInfo()
        {
            if (_disposed)
                throw new ObjectDisposedException(GetType().Name);

            int pageCount = NativeMethods.FPDF_GetPageCount(_document);
            var result = new List<SizeF>(pageCount);

            for (int i = 0; i < pageCount; i++)
            {
                double height;
                double width;
                NativeMethods.FPDF_GetPageSizeByIndex(_document, i, out width, out height);

                result.Add(new SizeF((float)width, (float)height));
            }

            return result;
        }

        public abstract void Save(Stream stream);

        protected void LoadDocument(IntPtr document)
        {
            _document = document;

            NativeMethods.FPDF_GetDocPermissions(_document);

            _formCallbacks = new NativeMethods.FPDF_FORMFILLINFO();
            _formCallbacksHandle = GCHandle.Alloc(_formCallbacks);
            _formCallbacks.version = 1;

            _form = NativeMethods.FPDFDOC_InitFormFillEnvironment(_document, ref _formCallbacks);
            NativeMethods.FPDF_SetFormFieldHighlightColor(_form, 0, 0xFFE4DD);
            NativeMethods.FPDF_SetFormFieldHighlightAlpha(_form, 100);

            NativeMethods.FORM_DoDocumentJSAction(_form);
            NativeMethods.FORM_DoDocumentOpenAction(_form);

            Bookmarks = new PdfBookmarkCollection();

            LoadBookmarks(Bookmarks, NativeMethods.FPDF_BookmarkGetFirstChild(document, IntPtr.Zero));
        }

        private void LoadBookmarks(PdfBookmarkCollection bookmarks, IntPtr bookmark)
        {
            if (bookmark == IntPtr.Zero)
                return;

            bookmarks.Add(LoadBookmark(bookmark));
            while ((bookmark = NativeMethods.FPDF_BookmarkGetNextSibling(_document, bookmark)) != IntPtr.Zero)
                bookmarks.Add(LoadBookmark(bookmark));
        }

        private PdfBookmark LoadBookmark(IntPtr bookmark)
        {
            var result = new PdfBookmark
            {
                Title = GetBookmarkTitle(bookmark),
                PageIndex = (int)GetBookmarkPageIndex(bookmark)
            };

            //Action = NativeMethods.FPDF_BookmarkGetAction(_bookmark);
            //if (Action != IntPtr.Zero)
            //    ActionType = NativeMethods.FPDF_ActionGetType(Action);

            var child = NativeMethods.FPDF_BookmarkGetFirstChild(_document, bookmark);
            if (child != IntPtr.Zero)
                LoadBookmarks(result.Children, child);

            return result;
        }

        private string GetBookmarkTitle(IntPtr bookmark)
        {
            uint length = NativeMethods.FPDF_BookmarkGetTitle(bookmark, null, 0);
            byte[] buffer = new byte[length];
            NativeMethods.FPDF_BookmarkGetTitle(bookmark, buffer, length);

            string result = Encoding.Unicode.GetString(buffer);
            return result.Substring(0, result.Length - 1);
        }

        private uint GetBookmarkPageIndex(IntPtr bookmark)
        {
            IntPtr dest = NativeMethods.FPDF_BookmarkGetDest(_document, bookmark);
            if (dest != IntPtr.Zero)
                return NativeMethods.FPDFDest_GetPageIndex(_document, dest);

            return 0;
        }

        public PdfMatches Search(string text, bool matchCase, bool wholeWord, int startPage, int endPage)
        {
            var matches = new List<PdfMatch>();

            for (int page = startPage; page <= endPage; page++)
            {
                using (var pageData = new PageData(_document, _form, page))
                {
                    NativeMethods.FPDF_SEARCH_FLAGS flags = 0;
                    if (matchCase)
                        flags |= NativeMethods.FPDF_SEARCH_FLAGS.FPDF_MATCHCASE;
                    if (wholeWord)
                        flags |= NativeMethods.FPDF_SEARCH_FLAGS.FPDF_MATCHWHOLEWORD;

                    var handle = NativeMethods.FPDFText_FindStart(pageData.TextPage, FPDFEncoding.GetBytes(text), flags, 0);

                    try
                    {
                        while (NativeMethods.FPDFText_FindNext(handle))
                        {
                            int index = NativeMethods.FPDFText_GetSchResultIndex(handle);

                            int matchLength = NativeMethods.FPDFText_GetSchCount(handle);

                            var result = new byte[(matchLength + 1) * 2];
                            NativeMethods.FPDFText_GetText(pageData.TextPage, index, matchLength, result);
                            string match = FPDFEncoding.GetString(result, 0, matchLength * 2);

                            double left, right, bottom, top;
                            NativeMethods.FPDFText_GetCharBox(pageData.TextPage, index, out left, out right, out bottom, out top);

                            matches.Add(new PdfMatch(
                                new PointF((float)left, (float)top),
                                match,
                                page
                            ));
                        }
                    }
                    finally
                    {
                        NativeMethods.FPDFText_FindClose(handle);
                    }
                }
            }

            return new PdfMatches(startPage, endPage, matches);
        }

        public void Dispose()
        {
            Dispose(true);

            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (_form != IntPtr.Zero)
                {
                    NativeMethods.FORM_DoDocumentAAction(_form, NativeMethods.FPDFDOC_AACTION.WC);
                    NativeMethods.FPDFDOC_ExitFormFillEnviroument(_form);
                    _form = IntPtr.Zero;
                }

                if (_document != IntPtr.Zero)
                {
                    NativeMethods.FPDF_CloseDocument(_document);
                    _document = IntPtr.Zero;
                }

                if (_formCallbacksHandle.IsAllocated)
                    _formCallbacksHandle.Free();

                _disposed = true;
            }
        }

       
        private class PageData : IDisposable
        {
            private readonly IntPtr _form;
            private bool _disposed;

            public IntPtr Page { get; private set; }

            public IntPtr TextPage { get; private set; }

            public double Width { get; private set; }

            public double Height { get; private set; }

            public PageData(IntPtr document, IntPtr form, int pageNumber)
            {
                _form = form;

                Page = NativeMethods.FPDF_LoadPage(document, pageNumber);
                TextPage = NativeMethods.FPDFText_LoadPage(Page);
                NativeMethods.FORM_OnAfterLoadPage(Page, form);
                NativeMethods.FORM_DoPageAAction(Page, form, NativeMethods.FPDFPAGE_AACTION.OPEN);

                Width = NativeMethods.FPDF_GetPageWidth(Page);
                Height = NativeMethods.FPDF_GetPageHeight(Page);
            }

            public void Dispose()
            {
                if (!_disposed)
                {
                    NativeMethods.FORM_DoPageAAction(Page, _form, NativeMethods.FPDFPAGE_AACTION.CLOSE);
                    NativeMethods.FORM_OnBeforeClosePage(Page, _form);
                    NativeMethods.FPDFText_ClosePage(TextPage);
                    NativeMethods.FPDF_ClosePage(Page);

                    _disposed = true;
                }
            }
        }
    }
}
