using Microsoft.VisualStudio.Commanding;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Editor.Commanding.Commands;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.Utilities;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;

namespace SurroundWithBrackets;

[Export(typeof(ICommandHandler))]
[Name(nameof(AutoSurroundCommandHandler))]
[ContentType("text")]
[TextViewRole(PredefinedTextViewRoles.PrimaryDocument)]
public class AutoSurroundCommandHandler : ICommandHandler<TypeCharCommandArgs> {

    private static readonly Dictionary<char, char> CharacterPairs = new Dictionary<char, char> {
        { '\'', '\'' },
        { '"', '"' },
        { '`', '`' },
        { '(', ')' },
        { '{', '}' },
        { '[', ']' }
    };


    private readonly ITextUndoHistoryRegistry _textUndoHistoryRegistry;


    [ImportingConstructor]
    public AutoSurroundCommandHandler(ITextUndoHistoryRegistry textUndoHistoryRegistry) {
        _textUndoHistoryRegistry = textUndoHistoryRegistry;
    }


    public string DisplayName => nameof(AutoSurroundCommandHandler);


    public CommandState GetCommandState(TypeCharCommandArgs args) {
        if (CharacterPairs.ContainsKey(args.TypedChar)) {
            return CommandState.Available;
        } else {
            return CommandState.Unavailable;
        }
    }


    public bool ExecuteCommand(TypeCharCommandArgs args, CommandExecutionContext executionContext) {
        if (!args.TextView.Selection.IsEmpty) {
            if (CharacterPairs.TryGetValue(args.TypedChar, out char closing)) {
                SurroundWith(args.TypedChar, closing, args.TextView);
                return true;
            }
        }

        return false;
    }


    private void SurroundWith(char opening, char closing, ITextView textView) {
        ITextUndoHistory history;


        history = _textUndoHistoryRegistry.GetHistory(textView.TextBuffer);

        using (ITextUndoTransaction transaction = history.CreateTransaction($"{opening}Auto Surround{closing}")) {
            IEnumerable<(int Position, char Character)> edits;
            bool changed;


            changed = false;

            // Pair the start of the span with the opening character and the end of the
            // span with the closing character, then sort the positions in descending order.
            // We need to do this in reverse order, because every character that we insert
            // will cause the points after it to no longer refer to the correct position.
            edits = textView
                .Selection
                .SelectedSpans
                .Where((x) => !x.IsEmpty)
                .SelectMany((x) => new[] { (x.Start.Position, Character: opening), (x.End.Position, Character: closing) })
                .OrderByDescending((x) => x.Position);

            foreach ((int Position, char Character) edit in edits) {
                textView.TextBuffer.Insert(edit.Position, edit.Character.ToString());
                changed = true;
            }

            // If there are multiple selections, but they are all empty,
            // the `Selection.IsEmpty` property returns false. We don't
            // edit empty selections, so it's possible that we didn't
            // actually make any changes. If that's the case, then we
            // can just cancel the undo transaction and exit.
            if (!changed) {
                transaction.Cancel();
                return;
            }

            // Now change the selected spans to account for
            // the new characters that have been inserted.
            textView.GetMultiSelectionBroker().PerformActionOnAllSelections((transformer) => {
                Selection selection;


                selection = transformer.Selection;

                if (!selection.IsEmpty) {
                    VirtualSnapshotPoint activePoint;
                    VirtualSnapshotPoint anchorPoint;
                    VirtualSnapshotPoint insertionPoint;


                    // Inserting a character at the start of a selected range does not cause
                    // the selection to include that character, but inserting a character
                    // at the end of a selected range causes the selection to be extended
                    // to include that character. We don't want to select that character,
                    // so we need to move the end of the selection back by one character.
                    if (selection.IsReversed) {
                        // For reversed selections, the active point (the end) is
                        // before the anchor point (the start), so to move the end
                        // of the selection back, we need to move the anchor point.
                        anchorPoint = new VirtualSnapshotPoint(selection.AnchorPoint.Position - 1);
                        activePoint = new VirtualSnapshotPoint(selection.ActivePoint.Position);

                    } else {
                        // For normal selections, the anchor point (the start) is
                        // before the active point (the end), so to move the end
                        // of the selection back, we need to move the active point.
                        anchorPoint = new VirtualSnapshotPoint(selection.AnchorPoint.Position);
                        activePoint = new VirtualSnapshotPoint(selection.ActivePoint.Position - 1);
                    }

                    // Keep the insertion point attached to the
                    // the same point that it curently is.
                    if (selection.InsertionPoint == selection.AnchorPoint) {
                        insertionPoint = anchorPoint;

                    } else if (selection.InsertionPoint == selection.ActivePoint) {
                        insertionPoint = activePoint;

                    } else {
                        insertionPoint = transformer.Selection.InsertionPoint;
                    }

                    transformer.MoveTo(anchorPoint, activePoint, insertionPoint, selection.InsertionPointAffinity);
                }
            });

            transaction.Complete();
        }
    }

}
