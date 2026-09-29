using System.Text;

namespace LangProcessor;

public partial class MainForm : Form
{
    private const string AppTitle = "Языковой процессор";
    private const string UntitledName = "Новый документ";
    private const float DefaultFontSize = 11f;
    private const string FileFilter =
        "Текстовые файлы (*.txt)|*.txt|" +
        "Все файлы (*.*)|*.*";

    private readonly LineNumberPanel _lineNumbers;
    private readonly ExprParser _parser = new();
    private string? _filePath;
    private string _savedText = "";
    private float _fontSize = DefaultFontSize;

    public MainForm() : this(Array.Empty<string>()) { }

    public MainForm(string[] args)
    {
        InitializeComponent();

        codeEditor.AllowDrop = true;
        codeEditor.DragEnter += OnDragEnter;
        codeEditor.DragDrop += OnDragDrop;
        codeEditor.ZoomRequested += (_, delta) => SetFontSize(_fontSize + delta);

        cmbFontSize.SelectedItem = ((int)DefaultFontSize).ToString();
        SetupGrid(tokenGrid, ("Условный код", 110), ("Тип лексемы", 260), ("Лексема", 160), ("Местоположение", 0));
        SetupGrid(tetradGrid, ("№", 60), ("Операция", 120), ("Аргумент 1", 160), ("Аргумент 2", 160), ("Результат", 0));

        _lineNumbers = new LineNumberPanel(codeEditor);
        splitContainer.Panel1.Controls.Add(_lineNumbers);

        var file = args.FirstOrDefault(File.Exists);
        if (file is not null) LoadFile(file);
        else NewDocument();
    }

    private bool IsModified => codeEditor.CurrentText != _savedText;
    private string DisplayName => _filePath is null ? UntitledName : Path.GetFileName(_filePath);

    private void OnEditorTextChanged(object? sender, EventArgs e) => UpdateUi();

    private void OnEditorSelectionChanged(object? sender, EventArgs e) => UpdateCaretStatus();

    private void UpdateUi()
    {
        Text = $"{AppTitle} — {DisplayName}{(IsModified ? " *" : "")}";
        btnUndo.Enabled = menuUndo.Enabled = codeEditor.CanUndoEdit;
        btnRedo.Enabled = menuRedo.Enabled = codeEditor.CanRedoEdit;
        UpdateCaretStatus();
    }

    private void UpdateCaretStatus()
    {
        var text = codeEditor.CurrentText;
        var pos = Math.Min(codeEditor.SelectionStart, text.Length);
        int line = 1, lineStart = 0, lines = 1;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n') continue;
            lines++;
            if (i < pos) { line++; lineStart = i + 1; }
        }

        var selected = codeEditor.SelectionLength;
        statusPosition.Text = $"Стр {line}, Стлб {pos - lineStart + 1}" + (selected > 0 ? $" (выделено {selected})" : "");
        statusLength.Text = $"Строк: {lines}   Символов: {text.Length}";
    }

    private void SetStatus(string text) => statusText.Text = text;

    private bool ConfirmSave()
    {
        if (!IsModified) return true;

        var answer = MessageBox.Show(this,
            $"Сохранить изменения в файле «{DisplayName}»?",
            AppTitle, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

        return answer switch
        {
            DialogResult.Yes => SaveDocument(saveAs: false),
            DialogResult.No => true,
            _ => false,
        };
    }

    private void NewDocument()
    {
        _filePath = null;
        codeEditor.LoadText("");
        _savedText = codeEditor.CurrentText;
        ClearResults();
        UpdateUi();
        codeEditor.Focus();
    }

    private void OnNew(object? sender, EventArgs e)
    {
        if (!ConfirmSave()) return;
        NewDocument();
        SetStatus("Создан новый документ");
    }

    private void OnOpen(object? sender, EventArgs e)
    {
        if (!ConfirmSave()) return;

        using var dialog = new OpenFileDialog { Title = "Открыть файл", Filter = FileFilter };
        if (_filePath is not null) dialog.InitialDirectory = Path.GetDirectoryName(_filePath);
        if (dialog.ShowDialog(this) == DialogResult.OK)
            LoadFile(dialog.FileName);
    }

    private bool LoadFile(string path)
    {
        string text;
        try
        {
            text = File.ReadAllText(path, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Не удалось открыть файл:\n{path}\n\n{ex.Message}",
                "Ошибка открытия", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        _filePath = Path.GetFullPath(path);
        codeEditor.LoadText(text);
        _savedText = codeEditor.CurrentText;
        ClearResults();
        UpdateUi();
        codeEditor.Focus();
        SetStatus($"Открыт файл: {_filePath}");
        return true;
    }

    private void OnSave(object? sender, EventArgs e) => SaveDocument(saveAs: false);

    private void OnSaveAs(object? sender, EventArgs e) => SaveDocument(saveAs: true);

    private bool SaveDocument(bool saveAs)
    {
        var path = _filePath;
        if (saveAs || path is null)
        {
            using var dialog = new SaveFileDialog
            {
                Title = "Сохранить как",
                Filter = FileFilter,
                DefaultExt = "txt",
                AddExtension = true,
                OverwritePrompt = true,
                FileName = path is null ? "program.txt" : Path.GetFileName(path),
            };
            if (path is not null) dialog.InitialDirectory = Path.GetDirectoryName(path);
            if (dialog.ShowDialog(this) != DialogResult.OK) return false;
            path = dialog.FileName;
        }

        try
        {
            var text = codeEditor.CurrentText;
            File.WriteAllText(path, text.Replace("\n", Environment.NewLine), new UTF8Encoding(false));
            _savedText = text;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Не удалось сохранить файл:\n{path}\n\n{ex.Message}",
                "Ошибка сохранения", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        _filePath = path;
        UpdateUi();
        SetStatus($"Сохранено: {path}");
        return true;
    }

    private void OnExit(object? sender, EventArgs e) => Close();

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!ConfirmSave()) e.Cancel = true;
    }

    private void OnDragEnter(object? sender, DragEventArgs e) =>
        e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] files) return;
        var path = files.FirstOrDefault(File.Exists);
        if (path is not null && ConfirmSave()) LoadFile(path);
    }

    private void EditCommand(Action<CodeEditor> action)
    {
        action(codeEditor);
        codeEditor.Focus();
        UpdateUi();
    }

    private void OnUndo(object? sender, EventArgs e) => EditCommand(ed => ed.UndoEdit());
    private void OnRedo(object? sender, EventArgs e) => EditCommand(ed => ed.RedoEdit());
    private void OnCut(object? sender, EventArgs e) => EditCommand(ed => ed.CutSelection());
    private void OnPaste(object? sender, EventArgs e) => EditCommand(ed => ed.PastePlain());
    private void OnDelete(object? sender, EventArgs e) => EditCommand(ed => ed.DeleteSelection());
    private void OnSelectAll(object? sender, EventArgs e) => EditCommand(ed => ed.SelectAll());

    private void OnCopy(object? sender, EventArgs e)
    {
        if (outputGrid.Focused && outputGrid.CurrentRow is { } l && l.Cells[2].Value is string message)
        {
            Clipboard.SetText($"{l.Cells[0].Value}\t{l.Cells[1].Value}\t{message}");
            return;
        }
        EditCommand(ed => { if (ed.SelectionLength > 0) ed.Copy(); });
    }

    private void SetFontSize(float size)
    {
        _fontSize = Math.Clamp(size, 7f, 40f);
        codeEditor.Font = new Font(codeEditor.Font.FontFamily, _fontSize);
        rpnText.Font = new Font(rpnText.Font.FontFamily, _fontSize);

        var text = ((int)_fontSize).ToString();
        _updatingFontCombo = true;
        cmbFontSize.SelectedItem = cmbFontSize.Items.Contains(text) ? text : null;
        _updatingFontCombo = false;
        SetStatus($"Размер шрифта: {_fontSize}");
    }

    private bool _updatingFontCombo;

    private void OnFontSizeSelected(object? sender, EventArgs e)
    {
        if (_updatingFontCombo || cmbFontSize.SelectedItem is not string text) return;
        SetFontSize(float.Parse(text));
        codeEditor.Focus();
    }

    private void OnZoomIn(object? sender, EventArgs e) => SetFontSize(_fontSize + 1);
    private void OnZoomOut(object? sender, EventArgs e) => SetFontSize(_fontSize - 1);
    private void OnZoomReset(object? sender, EventArgs e) => SetFontSize(DefaultFontSize);

    private void OnToggleWordWrap(object? sender, EventArgs e) => codeEditor.WordWrap = menuWordWrap.Checked;
    private void OnToggleLineNumbers(object? sender, EventArgs e) => _lineNumbers.Visible = menuLineNumbers.Checked;

    private void OnClearOutput(object? sender, EventArgs e) => ClearResults();

    private void ClearResults()
    {
        outputGrid.Rows.Clear();
        tokenGrid.Rows.Clear();
        tetradGrid.Rows.Clear();
        rpnText.Clear();
    }

    private void SetupGrid(DataGridView grid, params (string Header, int Width)[] columns)
    {
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.ReadOnly = true;
        grid.RowHeadersVisible = false;
        grid.MultiSelect = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.BackgroundColor = SystemColors.Window;
        grid.BorderStyle = BorderStyle.None;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersDefaultCellStyle = outputGrid.ColumnHeadersDefaultCellStyle.Clone();
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        grid.RowTemplate.Height = 24;
        grid.ScrollBars = ScrollBars.Both;
        grid.ShowCellToolTips = false;
        foreach (var (header, width) in columns)
        {
            var column = new DataGridViewTextBoxColumn { HeaderText = header, SortMode = DataGridViewColumnSortMode.NotSortable };
            if (width > 0) column.Width = width;
            else { column.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill; column.MinimumWidth = 150; }
            grid.Columns.Add(column);
        }
    }

    private static void AddSummaryRow(DataGridView grid, bool ok, params object[] values)
    {
        var index = grid.Rows.Add(values);
        var style = grid.Rows[index].DefaultCellStyle;
        style.BackColor = SystemColors.Control;
        style.SelectionBackColor = SystemColors.Control;
        style.ForeColor = ok ? Color.FromArgb(30, 120, 50) : Color.FromArgb(200, 30, 30);
        style.SelectionForeColor = style.ForeColor;
        style.Font = new Font(grid.Font, FontStyle.Bold);
    }

    private void OnRun(object? sender, EventArgs e)
    {
        ClearResults();
        var tokens = ExprLexer.Analyze(codeEditor.CurrentText);
        var result = _parser.Parse(tokens);

        foreach (var t in tokens)
        {
            var index = tokenGrid.Rows.Add(t.CodeText, t.Type, t.Text, t.Location);
            tokenGrid.Rows[index].Tag = (t.Offset, t.Length);
            if (t.IsError) tokenGrid.Rows[index].DefaultCellStyle.ForeColor = Color.FromArgb(200, 30, 30);
        }
        AddSummaryRow(tokenGrid, tokens.All(t => !t.IsError), "", $"Всего лексем: {tokens.Count}", "", "");

        foreach (var error in result.LexicalErrors.Concat(result.SyntaxErrors))
        {
            var index = outputGrid.Rows.Add(error.Fragment, error.Location, $"{error.Kind} ошибка: {error.Description}");
            outputGrid.Rows[index].Tag = (error.Offset, error.Length);
            outputGrid.Rows[index].DefaultCellStyle.ForeColor = Color.FromArgb(200, 30, 30);
        }
        if (result.Success)
        {
            var okIndex = outputGrid.Rows.Add("", "", "Лексических и синтаксических ошибок не обнаружено");
            outputGrid.Rows[okIndex].DefaultCellStyle.ForeColor = Color.FromArgb(30, 120, 50);
        }
        AddSummaryRow(outputGrid, result.Success, "Всего ошибок:", "",
            $"{result.LexicalErrors.Count + result.SyntaxErrors.Count} (лексических: {result.LexicalErrors.Count}, синтаксических: {result.SyntaxErrors.Count})");

        if (!result.Success)
        {
            AddSummaryRow(tetradGrid, false, "", "", "", "", "Тетрады не строятся: в выражении есть ошибки");
            rpnText.Text = "ПОЛИЗ не строится: в выражении есть лексические или синтаксические ошибки.";
            outputTabs.SelectedTab = tabErrors;
            SetStatus($"Найдены ошибки: лексических — {result.LexicalErrors.Count}, синтаксических — {result.SyntaxErrors.Count}");
        }
        else
        {
            foreach (var t in result.Tetrads)
                tetradGrid.Rows.Add(t.Number, t.Op, t.Arg1, t.Arg2, t.Result);
            AddSummaryRow(tetradGrid, true, "", "", "", "",
                result.Tetrads.Count == 0 ? $"Операций нет, значение выражения: {result.Result}" : $"Результат выражения: {result.Result}");
            rpnText.Text = BuildRpnReport(tokens);
            outputTabs.SelectedTab = tabTetrads;
            SetStatus($"Ошибок нет, тетрад: {result.Tetrads.Count}");
        }

        outputGrid.ClearSelection();
        tokenGrid.ClearSelection();
        tetradGrid.ClearSelection();
    }

    private static string BuildRpnReport(List<ExprToken> tokens)
    {
        var expression = string.Join(" ", tokens.Select(t => t.Text));
        var identifiers = tokens.Where(t => t.Code == ExprLexer.Identifier).Select(t => t.Text).Distinct().ToList();
        if (identifiers.Count > 0)
            return $"Выражение: {expression}\n\nПОЛИЗ строится и вычисляется только для выражений из целых чисел.\n" +
                   $"В выражении есть идентификаторы: {string.Join(", ", identifiers)}.";

        var rpn = Polish.Build(tokens);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Выражение: {expression}");
        sb.AppendLine($"ПОЛИЗ:     {string.Join(" ", rpn.Rpn)}");
        sb.AppendLine(rpn.Error is null ? $"Значение:  {rpn.Value}" : $"Ошибка вычисления: {rpn.Error}");
        sb.AppendLine();
        sb.AppendLine("Построение ПОЛИЗ (алгоритм Дейкстры):");
        var w = Math.Max(10, rpn.ConversionSteps.Max(x => x.Action.Length) + 2);
        sb.AppendLine($"{"Символ",-8}{"Действие".PadRight(w)}{"Стек операций",-16}Выход");
        foreach (var step in rpn.ConversionSteps)
            sb.AppendLine($"{step.Symbol,-8}{step.Action.PadRight(w)}{step.Stack,-16}{step.Output}");
        sb.AppendLine();
        sb.AppendLine("Вычисление ПОЛИЗ (стек):");
        sb.AppendLine($"{"Символ",-8}{"Действие",-28}Стек");
        foreach (var step in rpn.EvaluationSteps)
            sb.AppendLine($"{step.Symbol,-8}{step.Action,-28}{step.Stack}");
        return sb.ToString();
    }

    private void OnOutputCellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex >= 0 && outputGrid.Rows[e.RowIndex].Tag is ValueTuple<int, int> span)
            NavigateTo(span.Item1, span.Item2);
    }

    private void OnTokenCellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex >= 0 && tokenGrid.Rows[e.RowIndex].Tag is ValueTuple<int, int> span)
            NavigateTo(span.Item1, span.Item2);
    }

    private void OnOutputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter || outputGrid.CurrentRow?.Tag is not ValueTuple<int, int> span) return;
        e.Handled = true;
        NavigateTo(span.Item1, span.Item2);
    }

    private void NavigateTo(int offset, int length)
    {
        var total = codeEditor.TextLength;
        offset = Math.Min(offset, total);
        codeEditor.Select(offset, Math.Min(length, total - offset));
        codeEditor.ScrollToCaret();
        BeginInvoke(() => codeEditor.Focus());
    }

    private void OnTextSection(object? sender, EventArgs e)
    {
        var item = sender as ToolStripItem;
        var sections = HelpContent.TextSection(item?.Tag as string ?? "");
        InfoForm.Show(this, item?.Text ?? "", sections, new Size(720, 600));
    }

    private void OnHelp(object? sender, EventArgs e) =>
        InfoForm.Show(this, "Справка — " + AppTitle, HelpContent.UserGuide, new Size(720, 600));

    private void OnAbout(object? sender, EventArgs e) =>
        InfoForm.Show(this, "О программе", HelpContent.About, new Size(540, 400));
}
