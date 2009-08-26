﻿/*
 * Copyright (C) 2008, Robin Rosenberg <robin.rosenberg@dewire.com>
 * Copyright (C) 2008, Shawn O. Pearce <spearce@spearce.org>
 * Copyright (C) 2008, Marek Zawirski <marek.zawirski@gmail.com>
 *
 * All rights reserved.
 *
 * Redistribution and use in source and binary forms, with or
 * without modification, are permitted provided that the following
 * conditions are met:
 *
 * - Redistributions of source code must retain the above copyright
 *   notice, this list of conditions and the following disclaimer.
 *
 * - Redistributions in binary form must reproduce the above
 *   copyright notice, this list of conditions and the following
 *   disclaimer in the documentation and/or other materials provided
 *   with the distribution.
 *
 * - Neither the name of the Git Development Community nor the
 *   names of its contributors may be used to endorse or promote
 *   products derived from this software without specific prior
 *   written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND
 * CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES,
 * INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES
 * OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE
 * ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR
 * CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
 * SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT
 * NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
 * CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT,
 * STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)
 * ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF
 * ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using GitSharp.Exceptions;
using GitSharp.Util;

namespace GitSharp.Transport
{
    public class TransportHttp : HttpTransport, IWalkTransport
    {
        public static bool canHandle(URIish uri)
        {
            if (!uri.IsRemote)
            {
                return false;
            }
            string s = uri.Scheme;
            return "http".Equals(s) || "https".Equals(s) || "ftp".Equals(s);
        }

        private readonly Uri baseUrl;
        private readonly Uri objectsUrl;

        public TransportHttp(Repository local, URIish uri)
            : base(local, uri)
        {
            try
            {
                string uriString = uri.ToString();
                if (!uriString.EndsWith("/"))
                    uriString += "/";
                baseUrl = new Uri(uriString);
                objectsUrl = new Uri(baseUrl, "objects/");
            }
            catch (UriFormatException e)
            {
                throw new NotSupportedException("Invalid URL " + uri, e);
            }
        }

        public override IFetchConnection openFetch()
        {
            HttpObjectDB c = new HttpObjectDB(objectsUrl);
            WalkFetchConnection r = new WalkFetchConnection(this, c);
            r.available(c.readAdvertisedRefs());
            return r;
        }

        public override IPushConnection openPush()
        {
            string s = uri.Scheme;
            throw new NotSupportedException("Push not supported over " + s + ".");
        }

        public override void close()
        {
        }

        private class HttpObjectDB : WalkRemoteObjectDatabase
        {
            private readonly Uri objectsUrl;

            public HttpObjectDB(Uri b)
            {
                objectsUrl = b;
            }

            public override URIish getURI()
            {
                return new URIish(objectsUrl);
            }

            public override List<WalkRemoteObjectDatabase> getAlternates()
            {
                try
                {
                    return readAlternates(INFO_HTTP_ALTERNATES);
                }
                catch (FileNotFoundException)
                {   
                }

                try
                {
                    return readAlternates(INFO_ALTERNATES);
                }
                catch (FileNotFoundException)
                {
                }

                return null;
            }

            public override WalkRemoteObjectDatabase openAlternate(string location)
            {
                return new HttpObjectDB(new Uri(objectsUrl, location));
            }

            public override List<string> getPackNames()
            {
                List<string> packs = new List<string>();
                try
                {
                    StreamReader br = openReader(INFO_PACKS);
                    try
                    {
                        for (;;)
                        {
                            string s = br.ReadLine();
                            if (string.IsNullOrEmpty(s))
                                break;
                            if (!s.StartsWith("P pack-") || !s.EndsWith(".pack"))
                                throw invalidAdvertisement(s);
                            packs.Add(s.Substring(2));
                        }
                        return packs;
                    }
                    finally
                    {
                        br.Close();
                    }
                }
                catch (FileNotFoundException)
                {
                    return packs;
                }
            }

            public override Stream open(string path)
            {
                Uri @base = objectsUrl;
                Uri u = new Uri(@base, path);

                HttpWebRequest c = (HttpWebRequest) WebRequest.Create(u);
                HttpWebResponse response = (HttpWebResponse) c.GetResponse();

                switch (response.StatusCode)
                {
                    case HttpStatusCode.OK:
                        return response.GetResponseStream();
                        
                    case HttpStatusCode.NotFound:
                        throw new FileNotFoundException(u.ToString());

                    default:
                        throw new IOException(u + ": " + response.StatusDescription);
                }
            }

            public Dictionary<string, Ref> readAdvertisedRefs()
            {
                try
                {
                    StreamReader br = openReader(INFO_REFS);
                    try
                    {
                        return readAdvertisedImpl(br);
                    }
                    finally
                    {
                        br.Close();
                    }
                }
                catch (IOException err)
                {
                    try
                    {
                        throw new TransportException(new Uri(objectsUrl, INFO_REFS) + ": cannot read available refs", err);
                    }
                    catch (UriFormatException)
                    {
                        throw new TransportException(objectsUrl +  INFO_REFS + ": cannot read available refs", err);
                    }
                }
            }

            private static Dictionary<string, Ref> readAdvertisedImpl(TextReader br)
            {
                Dictionary<string, Ref> avail = new Dictionary<string, Ref>();
                for (;;)
                {
                    string line = br.ReadLine();
                    if (line == null)
                        break;

                    int tab = line.IndexOf('\t');
                    if (tab < 0)
                        throw invalidAdvertisement(line);

                    string name = line.Substring(tab + 1);
                    ObjectId id = ObjectId.FromString(line.Slice(0, tab));
                    if (name.EndsWith("^{}"))
                    {
                        name = name.Slice(0, name.Length - 3);
                        Ref prior = avail[name];
                        if (prior == null)
                            throw outOfOrderAdvertisement(name);

                        if (prior.PeeledObjectId != null)
                            throw duplicateAdvertisement(name + "^{}");

                        avail.Add(name, new Ref(Ref.Storage.Network, name, prior.ObjectId, id, true));
                    }
                    else
                    {
                        Ref prior = null;
                        if (avail.ContainsKey(name))
                        {
                            prior = avail[name];
                            avail[name] = new Ref(Ref.Storage.Network, name, id);
                        }
                        else
                        {
                            avail.Add(name, new Ref(Ref.Storage.Network, name, id));
                        }
                        if (prior != null)
                            throw duplicateAdvertisement(name);
                    }
                }
                return avail;
            }

            private static PackProtocolException outOfOrderAdvertisement(string n)
            {
                return new PackProtocolException("advertisement of " + n +"^{} came before " + n);
            }

            private static PackProtocolException invalidAdvertisement(string n)
            {
                return new PackProtocolException("invalid advertisement of " + n);
            }

            private static PackProtocolException duplicateAdvertisement(string n)
            {
                return new PackProtocolException("duplicate advertisements of " + n);
            }

            public override void close()
            {
            }
        }
    }
}