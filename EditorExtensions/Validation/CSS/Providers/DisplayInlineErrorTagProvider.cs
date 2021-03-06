﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.Composition;
using System.Globalization;
using System.Linq;
using Microsoft.CSS.Core;
using Microsoft.CSS.Editor;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Utilities;
using System.Reflection;
using System.Windows.Threading;

namespace MadsKristensen.EditorExtensions
{
    [Export(typeof(IWpfTextViewConnectionListener))]
    [ContentType(Microsoft.Web.Editor.CssContentTypeDefinition.CssContentType)]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    class DisplayInlineTextViewCreationListener : IWpfTextViewConnectionListener
    {
        public void SubjectBuffersConnected(IWpfTextView textView, ConnectionReason reason, Collection<ITextBuffer> subjectBuffers)
        {
            foreach (ITextBuffer buffer in subjectBuffers)
            {
                CssEditorDocument doc = CssEditorDocument.FromTextBuffer(buffer);
                doc.Tree.ItemsChanged += Tree_ItemsChanged;
                doc.Tree.TreeUpdated += Tree_TreeUpdated;
                InitializeCache(doc.Tree.StyleSheet);
            }
        }

        void Tree_TreeUpdated(object sender, CssTreeUpdateEventArgs e)
        {
            InitializeCache(e.Tree.StyleSheet);
        }

        private void InitializeCache(StyleSheet stylesheet)
        {
            _cache.Clear();

            var visitor = new CssItemCollector<Declaration>(true);
            stylesheet.Accept(visitor);

            foreach (Declaration dec in visitor.Items.Where(d => d.PropertyName != null))
            {
                if (dec.PropertyName.Text == "display" && dec.Values.Any(v => v.Text == "inline"))
                    _cache.Add(dec);
            }
        }

        public void SubjectBuffersDisconnected(IWpfTextView textView, ConnectionReason reason, Collection<ITextBuffer> subjectBuffers)
        {
            foreach (ITextBuffer buffer in subjectBuffers)
            {
                CssEditorDocument doc = CssEditorDocument.FromTextBuffer(buffer);
                doc.Tree.ItemsChanged -= Tree_ItemsChanged;
            }
        }

        private HashSet<Declaration> _cache = new HashSet<Declaration>();

        void Tree_ItemsChanged(object sender, CssItemsChangedEventArgs e)
        {
            CssTree tree = (CssTree)sender;

            foreach (ParseItem item in e.InsertedItems)
            {
                var visitor = new CssItemCollector<Declaration>(true);
                item.Accept(visitor);

                foreach (Declaration dec in visitor.Items)
                {
                    if (dec.PropertyName != null && dec.PropertyName.Text == "display" && dec.Values.Any(v => v.Text == "inline"))
                    {
                        _cache.Add(dec);
                                                
                        ParseItem rule = dec.Parent;
                        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => Update(rule, tree)), DispatcherPriority.Normal);
                    }
                }
            }

            foreach (ParseItem item in e.DeletedItems)
            {
                var visitor = new CssItemCollector<Declaration>(true);
                item.Accept(visitor);

                foreach (Declaration deleted in visitor.Items)
                {
                    if (_cache.Contains(deleted))
                    {
                        _cache.Remove(deleted);

                        ParseItem rule = deleted.Parent;
                        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => Update(rule, tree)), DispatcherPriority.Normal);
                    }
                }
            }
        }

        private static void Update(ParseItem rule, CssTree tree)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.InvokeMethod;
            object[] parameters = new object[3];
            parameters[0] = new ParseItemList();
            parameters[1] = new ParseItemList();
            parameters[2] = new ParseItemList() { rule };

            typeof(CssTree).InvokeMember("FireOnItemsChanged", flags, null, tree, parameters);
        }
    }

    [Export(typeof(ICssItemChecker))]
    [Name("DisplayInlineErrorTagProvider")]
    [Order(After = "Default Declaration")]
    internal class DisplayInlineErrorTagProvider : ICssItemChecker
    {
        private static readonly string[] _invalidProperties = new[] { "margin-top", "margin-bottom", "height", "width" };

        public ItemCheckResult CheckItem(ParseItem item, ICssCheckerContext context)
        {
            RuleBlock rule = (RuleBlock)item;

            if (!rule.IsValid || context == null)
                return ItemCheckResult.Continue;

            bool isInline = rule.Declarations.Any(d => d.PropertyName != null && d.PropertyName.Text == "display" && d.Values.Any(v => v.Text == "inline"));
            if (!isInline)
                return ItemCheckResult.Continue;

            IEnumerable<Declaration> invalids = rule.Declarations.Where(d => _invalidProperties.Contains(d.PropertyName.Text));

            foreach (Declaration invalid in invalids)
            {
                string error = string.Format(CultureInfo.InvariantCulture, Resources.BestPracticeInlineIncompat, invalid.PropertyName.Text);
                context.AddError(new SimpleErrorTag(invalid.PropertyName, error));
            }

            return ItemCheckResult.Continue;
        }

        public IEnumerable<Type> ItemTypes
        {
            get { return new[] { typeof(RuleBlock) }; }
        }
    }
}