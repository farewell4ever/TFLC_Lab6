namespace LangProcessor;

public sealed record RpnStep(string Symbol, string Action, string Stack, string Output);

public sealed record RpnResult(List<string> Rpn, List<RpnStep> ConversionSteps, List<RpnStep> EvaluationSteps, long? Value, string? Error);

public static class Polish
{
    private static readonly Dictionary<string, int> Priority = new()
    {
        ["+"] = 1,
        ["-"] = 1,
        ["*"] = 2,
        ["/"] = 2,
        ["%"] = 2,
    };

    public static RpnResult Build(List<ExprToken> tokens)
    {
        var (rpn, conversion) = ToRpn(tokens);
        var (value, evaluation, error) = Evaluate(rpn);
        return new RpnResult(rpn, conversion, evaluation, value, error);
    }

    private static (List<string>, List<RpnStep>) ToRpn(List<ExprToken> tokens)
    {
        var output = new List<string>();
        var stack = new Stack<string>();
        var steps = new List<RpnStep>();

        void Step(string symbol, string action) =>
            steps.Add(new RpnStep(symbol, action, string.Join(" ", stack.Reverse()), string.Join(" ", output)));

        foreach (var t in tokens)
        {
            var s = t.Text;
            if (t.Code == ExprLexer.Number)
            {
                output.Add(s);
                Step(s, "число — в выход");
            }
            else if (t.Code == ExprLexer.LeftParen)
            {
                stack.Push(s);
                Step(s, "'(' — в стек");
            }
            else if (t.Code == ExprLexer.RightParen)
            {
                while (stack.Peek() != "(") output.Add(stack.Pop());
                stack.Pop();
                Step(s, "выталкиваем операции до '(', скобки удаляем");
            }
            else
            {
                var popped = new List<string>();
                while (stack.Count > 0 && stack.Peek() != "(" && Priority[stack.Peek()] >= Priority[s])
                {
                    popped.Add(stack.Peek());
                    output.Add(stack.Pop());
                }
                stack.Push(s);
                Step(s, popped.Count == 0
                    ? $"операция '{s}' — в стек"
                    : $"выталкиваем {string.Join(" ", popped)} (приоритет не ниже), '{s}' — в стек");
            }
        }

        if (stack.Count > 0)
        {
            while (stack.Count > 0) output.Add(stack.Pop());
            Step("конец", "выталкиваем оставшиеся операции");
        }

        return (output, steps);
    }

    private static (long?, List<RpnStep>, string?) Evaluate(List<string> rpn)
    {
        var stack = new Stack<long>();
        var steps = new List<RpnStep>();

        void Step(string symbol, string action) =>
            steps.Add(new RpnStep(symbol, action, string.Join(" ", stack.Reverse()), ""));

        foreach (var s in rpn)
        {
            if (Priority.ContainsKey(s))
            {
                var b = stack.Pop();
                var a = stack.Pop();
                long r;
                try
                {
                    r = s switch
                    {
                        "+" => checked(a + b),
                        "-" => checked(a - b),
                        "*" => checked(a * b),
                        "/" => b == 0 ? throw new DivideByZeroException() : a / b,
                        _ => b == 0 ? throw new DivideByZeroException() : a % b,
                    };
                }
                catch (DivideByZeroException)
                {
                    Step(s, $"{a} {s} {b}: деление на ноль");
                    return (null, steps, "Деление на ноль");
                }
                catch (OverflowException)
                {
                    Step(s, $"{a} {s} {b}: переполнение");
                    return (null, steps, "Переполнение: результат не помещается в 64-битное целое");
                }
                stack.Push(r);
                Step(s, $"{a} {s} {b} = {r}");
            }
            else
            {
                if (!long.TryParse(s, out var n))
                    return (null, steps, $"Число {s} не помещается в 64-битное целое");
                stack.Push(n);
                Step(s, "число — в стек");
            }
        }

        return (stack.Pop(), steps, null);
    }
}
