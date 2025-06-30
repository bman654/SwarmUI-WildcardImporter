# Wildcard Importer Extension for SwarmUI

This extension allows you to import Wildcard collections that you've downloaded from Civit or elsewhere into SwarmUI.

It also adds a **Wildcard Importer Prompt Extensions** parameter group to the SwarmUI with some prompt utilities.
See the _Prompt Utilities_ section below for more information.

## Wildcard Importer

It can automatically convert the collections from
[SD Dynamic Prompt format](https://github.com/adieyal/sd-dynamic-prompts/blob/main/docs/SYNTAX.md#wildcards)
format into SwarmUI's wildcard prompt format.

# Usage Instructions

It is as easy as counting to 5.

----
<img style="float: right; width: 45%; margin: 0 0 10px 10px;" src="./docs/download.png" alt="Wildcard Collection Download">

First, download a Wildcard collection from Civit or elsewhere.

<div style="clear: both;"></div>

---

<img style="float: right; width: 45%; margin: 0 0 10px 10px;" src="./docs/importer.png" alt="Wildcard Importer">

Second, switch to the Wildcard Importer tab in SwarmUI and drag your downloaded file into the dropzone.
The importer supports: `.txt`, `.yaml` and `.zip` files.

Third, click the "Process Wildcards" button and wait for the import to complete.

<div style="clear: both;"></div>

---

<img style="float: right; width: 45%; margin: 0 0 10px 10px;" src="./docs/refresh.png" alt="Refresh and Generate">

Fourth, switch back to Generate tab and goto Wildcards tab at bottom and click the Refresh button to see the imported
collection.

<div style="clear: both;"></div>

Fifth, enter a wildcard prompt and click Generate!

## Prompt Utilities

<img style="float: right; width: 45%; margin: 0 0 10px 10px;" src="./docs/prompt-utilities-1.png">
The extension adds a new section to the generation parameters.  Currently there is only one new item in this section.

* Cleanup Prompts - if enabled, then your prompts will be cleaned up.  Extraneous whitespace will be removed.  Newlines will be replaced with spaces.  Unnecessary commas will be removed.  This is really useful when working with wildcards which occsioanlly inject multiple commas.

<div style="clear: both;"></div>
