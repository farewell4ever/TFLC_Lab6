namespace LangProcessor;

public sealed record ExprError(string Kind, string Fragment, int Line, int Column, int Offset, int Length, string Description)
{
    public string Location => $"строка {Line}, позиция {Column}";
}

public sealed record Tetrad(int Number, string Op, string Arg1, string Arg2, string Result);

public sealed record ExprParseResult(List<ExprError> LexicalErrors, List<ExprError> SyntaxErrors, List<Tetrad> Tetrads, string Result)
{
    public bool Success => LexicalErrors.Count == 0 && SyntaxErrors.Count == 0;
}

public sealed class ExprParser
{
    private List<ExprToken> _tokens = new();
    private List<ExprError> _syntax = new();
    private List<Tetrad> _tetrads = new();
    private int _pos;
    private int _temp;

    public ExprParseResult Parse(List<ExprToken> tokens)
    {
        _tokens = tokens;
        _syntax = new List<ExprError>();
        _tetrads = new List<Tetrad>();
        _pos = 0;
        _temp = 0;

        var lexical = tokens.Where(t => t.IsError)
            .Select(t => new ExprError("Лексическая", t.Text, t.Line, t.Column, t.Offset, t.Length, $"Недопустимый символ '{t.Text}'"))
            .ToList();

        if (!tokens.Any(t => !t.IsError))
        {
            if (lexical.Count == 0)
                _syntax.Add(new ExprError("Синтаксическая", "(пусто)", 1, 1, 0, 0, "Пустое выражение: ожидается операнд"));
            return new ExprParseResult(lexical, _syntax, new List<Tetrad>(), "");
        }

        var result = E();
        while (!AtEnd)
        {
            SkipInvalid();
            if (AtEnd) break;
            var t = Current!;
            if (t.Code == ExprLexer.RightParen)
            {
                SyntaxError(t, "Лишняя закрывающая скобка ')'");
                _pos++;
            }
            else if (StartsOperand(t))
            {
                SyntaxError(t, $"Пропущен знак операции перед '{t.Text}'");
                result = A(B(F()));
            }
            else
            {
                SyntaxError(t, $"Неожиданный символ '{t.Text}'");
                _pos++;
            }
        }

        var ok = lexical.Count == 0 && _syntax.Count == 0;
        return new ExprParseResult(lexical, _syntax, ok ? _tetrads : new List<Tetrad>(), ok ? result : "");
    }

    private ExprToken? Current => _pos < _tokens.Count ? _tokens[_pos] : null;
    private bool AtEnd => _pos >= _tokens.Count;

    private void SkipInvalid()
    {
        while (Current is { IsError: true }) _pos++;
    }

    private static bool StartsOperand(ExprToken t) =>
        t.Code is ExprLexer.Number or ExprLexer.Identifier or ExprLexer.LeftParen;

    private bool Is(params int[] codes)
    {
        SkipInvalid();
        return Current is { } t && codes.Contains(t.Code);
    }

    private string E()
    {
        var left = T();
        return A(left);
    }

    private string A(string left)
    {
        if (!Is(ExprLexer.Plus, ExprLexer.Minus)) return left;
        var op = _tokens[_pos++].Text;
        var right = T();
        return A(Emit(op, left, right));
    }

    private string T()
    {
        var left = F();
        return B(left);
    }

    private string B(string left)
    {
        if (!Is(ExprLexer.Multiply, ExprLexer.Divide, ExprLexer.Modulo)) return left;
        var op = _tokens[_pos++].Text;
        var right = F();
        return B(Emit(op, left, right));
    }

    private string F()
    {
        SkipInvalid();
        var t = Current;

        if (t is null)
        {
            MissingAtEnd("Пропущен операнд в конце выражения");
            return "?";
        }

        switch (t.Code)
        {
            case ExprLexer.Number:
            case ExprLexer.Identifier:
                _pos++;
                return t.Text;

            case ExprLexer.LeftParen:
                _pos++;
                var inner = E();
                if (Is(ExprLexer.RightParen)) _pos++;
                else if (AtEnd) MissingAtEnd($"Не закрыта скобка '(' (строка {t.Line}, позиция {t.Column}): ожидается ')'");
                else SyntaxError(Current!, $"Ожидается ')' для скобки из строки {t.Line}, позиции {t.Column}");
                return inner;

            case ExprLexer.RightParen:
                SyntaxError(t, "Пропущен операнд перед ')'");
                return "?";

            default:
                SyntaxError(t, $"Пропущен операнд перед '{t.Text}'");
                return "?";
        }
    }

    private string Emit(string op, string arg1, string arg2)
    {
        var result = $"t{++_temp}";
        _tetrads.Add(new Tetrad(_tetrads.Count + 1, op, arg1, arg2, result));
        return result;
    }

    private void SyntaxError(ExprToken token, string description)
    {
        if (_syntax.Any(e => e.Offset == token.Offset && e.Length == token.Length)) return;
        _syntax.Add(new ExprError("Синтаксическая", token.Text, token.Line, token.Column, token.Offset, token.Length, description));
    }

    private void MissingAtEnd(string description)
    {
        var last = _tokens[^1];
        var offset = last.Offset + last.Length;
        if (_syntax.Any(e => e.Offset == offset && e.Length == 0)) return;
        _syntax.Add(new ExprError("Синтаксическая", "конец выражения", last.Line, last.Column + last.Length, offset, 0, description));
    }
}
