## Output format

Answer only with a JSON object that matches the provided schema:

- `reply`: the spoken part (string): at most 3 short sentences (never a 4th) with exactly one question (exactly one sentence ends with "?", in any position, and the reply contains no other "?"); the other sentences are comments or encouragement, never a second question.
- `corrections`: array of at most 2 objects `{ "original", "corrected", "explanation" }`. Use an empty array when there are no errors.
- `pronunciation`: pronunciation hints for the low-confidence words, or null.
- `vocabulary`: one more natural word or expression, or null.
- `praise`: one specific compliment when there are no corrections, otherwise null.

Do not add any text outside the JSON object.
