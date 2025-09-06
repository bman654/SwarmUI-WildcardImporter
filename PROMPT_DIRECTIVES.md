# Wildcard Importer Prompt Directives

This extension adds several custom prompt directives that extend SwarmUI's built-in prompt syntax capabilities.
These directives are processed during prompt parsing and provide additional functionality
for managing prompts, variables, and conditional logic.

Most of the directives are most useful when used within Wildcards, but they can also be used in any prompt.

## Enhanced Random Selection

### `<wcrandom>`

An enhanced version of SwarmUI's built-in `<random>` directive with support for weighted choices and custom separators.
Like `<random>`, the options in `<wcrandom>` can be separated by `,` or `|` or `||`.

**Basic Syntax:**
```
<wcrandom:option1,option2,option3>
```

**With Count and Separator:**
```
<wcrandom[1-3]:option1|option2|option3>
<wcrandom[2,]:option1|option2|option3> // separator is ", "
<wcrandom[1-3, and ]:option1||option2||option3> // separator is " and "
```

**Weighted Options:**
```
<wcrandom:0.3::rare option|6::common option|normal option>
```

**Features:**
- **Weighted Selection**: Use `weight::option` syntax to control probability
- **Custom Separators**: Specify separator after count (default is `' '`)
- **Multiple Selections**: Use `[count]` or `[min-max]` to select multiple options
- **No Repetition**: Avoids repeating the same option unless count exceeds available options

**Examples:**
- `<wcrandom[2]:red|blue|green>` → might return `"red blue"`
- `<wcrandom[1-3,]:apple|banana|cherry>` → might return `"apple, banana"`
- `<wcrandom:0.1::legendary|1::rare|10::common>` → heavily favors "common"

## Enhanced Wildcard Selection

### `<wcwildcard>`

An enhanced version of SwarmUI's built-in `<wildcard>` directive with support for custom separators and advanced count/range syntax.

**Basic Syntax:**
```
<wcwildcard:cardname>
```

**With Count and Custom Separators:**
```
<wcwildcard[2]:cardname>
<wcwildcard[1-3]:cardname>
<wcwildcard[2,]:cardname> // separator is ", "
<wcwildcard[1-3, and ]:cardname> // separator is " and "
```

**With Exclusions:**
```
<wcwildcard:cardname,not=option1|option2>
<wcwildcard[2]:cardname,not=unwanted1|unwanted2>
```

**Features:**
- **Custom Separators**: Specify separator after count (default is `' '`)
- **Multiple Selections**: Use `[count]` or `[min-max]` to select multiple options
- **Option Exclusion**: Use `,not=option1|option2` to exclude specific wildcard options
- **No Repetition**: Avoids repeating the same option unless count exceeds available options

**Examples:**
- `<wcwildcard[2]:animals>` → might return `"cat dog"`
- `<wcwildcard[1-3,]:colors>` → might return `"red, blue, green"`
- `<wcwildcard[2, and ]:styles>` → might return `"realistic and detailed"`
- `<wcwildcard:characters,not=villain|monster>` → excludes villain and monster options

## Negative Prompt Management

### `<wcnegative>`

Dynamically adds content to the negative prompt during generation.

**Syntax:**
```
<wcnegative:text to append to negative prompt>
<wcnegative[prepend]:text to prepend to negative prompt>
```

**Behavior:**
- By default, appends text to the end of the current negative prompt
- Use `[prepend]` to add text to the beginning of the negative prompt

**Examples:**
- `<wcnegative:blurry, low quality>` → adds to end of negative prompt
- `<wcnegative[prepend]:worst quality, >` → adds to beginning of negative prompt

## Variable Management

### `<wcaddvar>`

Appends or prepends content to existing variables.

**Syntax:**
```
<wcaddvar[variable_name]:content to add>
<wcaddvar[variable_name,prepend]:content to prepend>
```

**Behavior:**
- Modifies existing variables created with `<setvar>`
- By default appends content to the variable's current value
- Use `prepend` mode to add content to the beginning
- Creates the variable if it doesn't exist
- Unlike `<setvar>`, `<wcaddvar>` never directly adds to the prompt.

**Examples:**
- `<setvar[style]:portrait>` then `<wcaddvar[style]:, detailed>` → style becomes "portrait, detailed"
- `<wcaddvar[tags,prepend]:masterpiece, >` → prepends to existing tags variable

## Macro Management

### `<wcaddmacro>`

Appends or prepends content to existing macros.

**Syntax:**
```
<wcaddmacro[macro_name]:content to add>
<wcaddmacro[macro_name,prepend]:content to prepend>
```

**Behavior:**
- Modifies existing macros created with `<setmacro>`
- By default appends content to the macro's current value
- Use `prepend` mode to add content to the beginning
- Creates the macro if it doesn't exist
- Does not force an immediate evaluation of the macro or the content being added.
- Unlike `<setmacro>`, `<wcaddmacro>` never directly adds to the prompt.

**Examples:**
- `<setmacro[color]:<random:red|blue|green>>` then `<wcaddmacro[color]:, vibrant>` → adds ", vibrant" to each color selection
- `<wcaddmacro[quality,prepend]:best quality, >` → prepends to existing quality macro

## Conditional Logic

### `<wcmatch>` and `<wccase>`

Provides conditional logic for prompts using expression evaluation.
A `<wcmatch>` block will render the first matching `<wccase>` block's content.
All other `<wccase>` blocks will be ignored.
A `<wccase>` block with no condition will be treated as the default case and will always match if no previous cases match.

**Syntax:**
```
<wcmatch:
  <wccase[condition expression 1]:content if condition1 is true>
  <wccase[condition expression 2]:content if condition2 is true>
  <wccase:default content if no conditions match>
>
```

**Condition Expression Support:**
- Variable comparisons: `myvar == "value"`
- Logical operators: `&&` (and), `||` (or)
- Grouping: `(` and `)`
- String contains functions
- Comparison operators:
  - `"string" == myvar` (case-sensitive equality)
  - `"string" ~= myvar` (case-sensitive inequality)
  - `contains(myvar, "text")` (case-sensitive partial match)
  - `icontains(myvar, "text")` (case-insensitive partial match)
  - `length(myvar) > 10` (length of string greater than)
  - `length(myvar) < 10` (length of string less than)
  - `length(myvar) >= 10` (length of string greater than or equal to)
  - `length(myvar) <= 10` (length of less than or equal to)
**Behavior:**
- Evaluates conditions in order
- Returns content from first matching condition
- Falls back to default case (no condition) if no matches
- Only one case will be processed per match block

**Examples:**

**Simple Variable Matching:**
```
<setvar[mood,false]:<random:happy|sad|angry>>
<wcmatch:
  <wccase[mood == "happy"]:smiling, cheerful>
  <wccase[mood == "sad"]:crying, melancholy>
  <wccase:neutral expression>
>
```

**Complex Conditions:**
```
<setvar[style,false]:<random:realistic|anime|cartoon>>
<setvar[gender,false]:<random:male|female>>
<wcmatch:
  <wccase[style == "anime" && gender == "female"]:kawaii, moe, detailed anime girl>
  <wccase[contains(style, "real") && gender == "male"]:photorealistic man, detailed>
  <wccase[icontains(style, "CARTOON")]:colorful cartoon character>
  <wccase:default artistic style>
>
```

## Usage Tips

1. **Combining Directives**: These directives can be combined with SwarmUI's built-in syntax:
   ```
   <setvar[character,false]:<wcrandom:warrior|mage|rogue>>
   <wcmatch:
     <wccase[character == "warrior"]:heavy armor, sword<wcnegative:, magic>>
     <wccase[character == "mage"]:robes, staff<wcnegative:, physical weapons>>
     <wccase:leather armor, dagger>
   >
   ```

2. **Dynamic Negative Prompts**: Use conditional logic to modify negative prompts based on other choices:
   ```
   <setvar[lighting,false]:<random:bright|dark|neon>>
   <wcmatch:
     <wccase[lighting == "dark"]:moody atmosphere<wcnegative:, bright, overexposed>>
     <wccase[lighting == "neon"]:cyberpunk vibes<wcnegative:, natural lighting>>
     <wccase:natural lighting<wcnegative:, artificial, neon>>
   >
   ```

3. **Building Complex Variables**: Use `wcaddvar` to build up complex descriptions:
   ```
   <setvar[description,false]:a woman>
   <wcaddvar[description]:, <wcrandom:2::beautiful|gorgeous|stunning|pretty>>
   <wcaddvar[description]:, wearing <random:dress|shirt|blouse>>
   <var:description>
   ```
