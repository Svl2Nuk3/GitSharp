	    public virtual int getParentCount()
        /// <summary>
        ///Convert the patch script for this file into a string.
        /// </summary>
        /// <param name="oldCharset">hint character set to decode the old lines with.</param>
        /// <param name="newCharset">hint character set to decode the new lines with.</param>
        /// <returns>the patch script, as a Unicode string.</returns>
	    public virtual String getScriptText(Encoding oldCharset, Encoding newCharset)
        /// <summary>
        /// Convert the patch script for this file into a string.
        /// </summary>
        /// <param name="charsetGuess">
        /// optional array to suggest the character set to use when
        /// decoding each file's line. If supplied the array must have a
        /// length of <code>{@link #getParentCount()} + 1</code>
        /// representing the old revision character sets and the new
        /// revision character set.
        /// </param>
        /// <returns>the patch script, as a Unicode string.</returns>
	    public virtual String getScriptText(Encoding[] charsetGuess)
	    public virtual FileMode getOldMode()
	    public virtual AbbreviatedObjectId getOldId()
	    public virtual HunkHeader newHunkHeader(int offset)
	    public virtual int parseGitHeaders(int ptr, int end)
        public virtual void parseNewFileMode(int ptr, int eol)

                if (RawParseUtils.match(buf, ptr, OLD_NAME) >= 0)

        public virtual void parseIndexLine(int ptr, int end)