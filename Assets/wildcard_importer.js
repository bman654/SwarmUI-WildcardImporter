document.addEventListener("DOMContentLoaded", function () {
  initializeWildcardImporter();
  Dropzone.options.wildcardImporterDropzone = {
    // Configuration options go here
    autoProcessQueue: false,
    uploadMultiple: true,
    parallelUploads: 100,
    maxFiles: 100,
    url: "",
    init: function () {
      var myDropzone = this;

      // First change the button to actually tell Dropzone to process the queue.
      this.element
        .querySelector("button[id=wildcardImporter-process-wildcards]")
        .addEventListener("click", function (e) {
          // Make sure that the form isn't actually being sent.
          e.preventDefault();
          e.stopPropagation();
          myDropzone.processQueue();
        });

      // Listen to the sendingmultiple event. In this case, it's the sendingmultiple event instead
      // of the sending event because uploadMultiple is set to true.
      this.on("sendingmultiple", function () {
        // Gets triggered when the form is actually being sent.
        // Hide the success button or the complete form.
      });
      this.on("successmultiple", function (files, response) {
        // Gets triggered when the files have successfully been sent.
        // Redirect user or notify of success.
      });
      this.on("errormultiple", function (files, response) {
        // Gets triggered when there was an error sending the files.
        // Maybe show form again, and notify user of error
      });
    },
  };
});

function initializeWildcardImporter() {
  const dropzone = document.getElementById("wildcardImporter-dropzone");
  const fileInput = document.getElementById("wildcardImporter-file-input");
  const processButton = document.getElementById(
      "wildcardImporter-process-wildcards"
  );
  const statusDiv = document.getElementById(
      "wildcardImporter-processing-status"
  );
  const historyDiv = document.getElementById(
      "wildcardImporter-processing-history"
  );

  let files = [];

  // Handle click on dropzone to trigger file input
  dropzone.addEventListener("click", () => fileInput.click());

  // Handle dragover event to provide visual feedback
  dropzone.addEventListener("dragover", (e) => {
    e.preventDefault();
    dropzone.classList.add("dragover");
  });

  // Handle dragleave event to remove visual feedback
  dropzone.addEventListener("dragleave", () => {
    dropzone.classList.remove("dragover");
  });

  // Handle drop event to capture files
  dropzone.addEventListener("drop", (e) => {
    e.preventDefault();
    dropzone.classList.remove("dragover");
    files = Array.from(e.dataTransfer.files);
    updateDropzoneText();
  });

  // Handle file selection via file input
  fileInput.addEventListener("change", () => {
    files = Array.from(fileInput.files);
    updateDropzoneText();
  });

  // Handle process wildcards button click
  processButton.addEventListener("click", () => {
    if (files.length > 0) {
      processWildcards(files);
    } else {
      alert("Please select files to process.");
    }
  });

  // Update dropzone text based on selected files
  function updateDropzoneText() {
    if (files.length === 0) {
      dropzone.textContent = "Drop files here or click to select";
      return;
    }

    // update name input if it is currently empty and the user uploaded something other than a single TXT file
    const nameElement = document.getElementById("wildcardImporter-name");
    if (!nameElement.value?.trim() && (files.length > 1 || !files[0].name.toLowerCase().endsWith(".txt"))) {
      nameElement.value = files[0].name.replace(/\.[^/.]*$/, "");
    }


    if (files.length === 1) {
      dropzone.textContent = files[0].name;
      return;
    }

    dropzone.textContent = `${files.length} files: ${files.map(file => file.name).join(", ")}`;
  }

  // Initialize destination folder and processing history
  updateDestinationFolder();
  updateHistory();

  /**
   * Processes wildcard files by sending their Base64-encoded content to the backend.
   * @param {FileList | File[]} filesToProcess - An array or FileList of File objects to process.
   */
  async function processWildcards(filesToProcess) {
    try {
      // Convert the FileList or Array of Files to an Array and map each to a FileData object
      const fileDataArray = await Promise.all(
          Array.from(filesToProcess).map(async (file) => {
            const base64Content = await readFileAsBase64(file);
            return {
              FilePath: file.name, // Using file.name as the FilePath
              Base64Content: base64Content,
            };
          })
      );

      // Serialize the array of FileData objects into a JSON string
      const filesJson = JSON.stringify(fileDataArray);
      const name = document.getElementById("wildcardImporter-name").value;

      // Prepare the data payload with the serialized JSON string
      const payload = {filesJson, name};

      // Send the payload to the 'ProcessWildcards' API endpoint
      genericRequest(
          "ProcessWildcards",
          payload,
          (data) => {
            if (data.success) {
              updateStatus(data.taskId);
            } else {
              alert("Error: " + data.message);
            }
          },
          true
      );

      // clear the form
      document.getElementById("wildcardImporter-file-input").value = "";
      document.getElementById("wildcardImporter-name").value = "";
      document.getElementById("wildcardImporter-dropzone").textContent = "Drop files here or click to select";
      files = [];
    } catch (error) {
      console.error("Error processing wildcards:", error);
      alert("An error occurred while processing the files. Please try again.");
    }
  }
}

/**
 * Reads a File object and returns its Base64-encoded content.
 * @param {File} file - The File object to read.
 * @returns {Promise<string>} - A promise that resolves to the Base64-encoded content of the file.
 */
function readFileAsBase64(file) {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();

    // Define the onload handler to resolve the promise with Base64 content
    reader.onload = () => {
      // reader.result is a data URL of the form "data:<mime>;base64,<data>"
      const base64 = reader.result.split(",")[1]; // Extract the Base64 part
      resolve(base64);
    };

    // Define the onerror handler to reject the promise on failure
    reader.onerror = () => {
      reader.abort(); // Abort the read operation
      reject(new Error("Failed to read file: " + file.name));
    };

    // Start reading the file as a Data URL
    reader.readAsDataURL(file);
  });
}

function updateStatus(taskId) {
  genericRequest("GetProcessingStatus", { taskId: taskId }, (data) => {
    const statusDiv = document.getElementById(
      "wildcardImporter-processing-status"
    );
    
    // Prepare the main status content
    let statusContent = `
          <h4>Processing Status</h4>
          <p>Status: <span class="wildcard-status-${data.Status.toLowerCase()}">${data.Status}</span></p>
          <p>Read Progress: ${data.InfilesProcessed} of ${data.Infiles} files examined</p>
          <p>Write Progress: ${data.OutfilesProcessed} of ${data.Outfiles} files written</p>`
    
    statusDiv.innerHTML = statusContent;
    
    if (data.Warnings?.length > 0) {
      statusDiv.innerHTML += `<h4>Warnings</h4>`;
      
      const warningDiv = document.createElement("div");
      data.Warnings.forEach((warning) => {
        makeDiv(warning, warningDiv).classList.add("warn");
      });
      statusDiv.appendChild(warningDiv);
    }

    if (data.Conflicts?.length > 0) {
      statusDiv.innerHTML += `<h4>Conflicts</h4>`;
      data.Conflicts.forEach((conflict) => {
        statusDiv.innerHTML += `
                  <p>Conflict for file: ${conflict.filePath}</p>
                  <button onclick="wildcardImporterResolveConflict('${taskId}', '${conflict.filePath}', 'overwrite')">Overwrite</button>
                  <button onclick="wildcardImporterResolveConflict('${taskId}', '${conflict.filePath}', 'rename')">Rename</button>
                  <button onclick="wildcardImporterResolveConflict('${taskId}', '${conflict.filePath}', 'skip')">Skip</button>
              `;
      });
    }

    if (data.Status === "InProgress") {
      setTimeout(() => updateStatus(taskId), 1000);
    } else {
      updateHistory();
    }
  });
}

function wildcardImporterResolveConflict(taskId, filePath, resolution) {
  genericRequest(
    "ResolveConflict",
    { taskId: taskId, filePath: filePath, resolution: resolution },
    (data) => {
      if (data.success) {
        alert("Conflict resolved");
        updateStatus(taskId);
      } else {
        alert("Error resolving conflict: " + data.message);
      }
    }
  );
}

function undoProcessing() {
  genericRequest("UndoProcessing", {}, (data) => {
    if (data.success) {
      alert("Processing undone successfully");
      updateHistory();
    } else {
      alert("Error undoing processing: " + data.message);
    }
  });
}

function makeDiv(text, parent) {
  const div = document.createElement("div");
  div.textContent = text;
  parent.appendChild(div);
  return div;
}

function updateHistory() {
  genericRequest("GetProcessingHistory", {}, (data) => {
    const historyDiv = document.getElementById(
      "wildcardImporter-processing-history"
    );
    historyDiv.innerHTML = "<h4>Processing History</h4>";
    if (data.history && data.history.length > 0) {
      const historyList = document.createElement("div");
      historyList.classList.add("history-list");
      data.history.forEach(item => {
        makeDiv("", historyList).classList.add(item.Success ? (item.Warnings.length > 0 ? "warn" : "pass") : "fail");
        makeDiv(new Date(item.Timestamp).toLocaleString(), historyList)
        makeDiv(item.Name, historyList)
        makeDiv(`${item.Warnings.length} warnings`, historyList).title = item.Warnings.join("\n");
        makeDiv(item.Description, historyList)
      });
      historyDiv.appendChild(historyList);
    } else {
      historyDiv.innerHTML += "<p>No processing history available.</p>";
    }
  });
}

function updateDestinationFolder() {
  // TODO: Allow setting the destination folder via API. Users might want to set it to their own custom folder.
  const destinationFolder = document.getElementById(
    "wildcardImporter-destination-folder"
  );
  genericRequest("GetDestinationFolder", {}, (data) => {
    destinationFolder.textContent = data.folderPath;
  });
}


const WCDetailerCompletionTypeNone = "";
const WCDetailerCompletionTypeParam = "0";
const WCDetailerCompletionTypeYolo = "1";
const WCDetailerCompletionTypeFunction = "4";

class WCDetailerCompletionItem {
  constructor(text, displayText, description, type) {
    this.tag = type;
    this.raw = true;
    this.name = text;
    this.clean = displayText;
    this.desc = description;
  }

  startsWith() {
    // implement this method to fake out promptTabComplete logic that expects a string
    return true;
  }
}
promptTabComplete.registerPrefix('wcdetailer', 'Automatically segment an area by CLIP matcher and set operations and inpaint it (optionally with a unique prompt)', (suffix, prompt) => {
  // suffix is the text that comes after the ':' in '<wcdetailer:...>'
  const origSuffix = suffix;
  suffix = suffix.trim();
  
  // Helper function to create completion items with proper text/displayText
  function createCompletion(completionText, displayText, description, type) {
    const index = promptTabComplete.findLastWordIndex(prompt);
    return new WCDetailerCompletionItem(`${prompt.substring(index)}${completionText}`, displayText, description, type);
  }
  
  // Helper function to check if a string matches a prefix (case insensitive)
  function matchesPrefix(text, prefix) {
    return text.toLowerCase().startsWith(prefix.toLowerCase());
  }
  
  // Helper function to generate mask specifier completions
  function getMaskSpecifierCompletions() {
    const completions = [];
    
    // Add unary operators
    completions.push(createCompletion('!', '!', 'Invert (NOT) operation', WCDetailerCompletionTypeNone));
    completions.push(createCompletion('(', '(', 'Group expressions', WCDetailerCompletionTypeNone));
    
    // Add functions
    const functions = ['box', 'circle', 'hull', 'oval'];
    functions.forEach(func => {
      completions.push(createCompletion(`${func}(`, `${func}(`, `Define a ${func}-shaped mask`, WCDetailerCompletionTypeFunction));
    });
    
    // Add YOLO models
    const yoloModels = rawGenParamTypesFromServer.find(p => p.id == 'yolomodelinternal')?.values || [];
    yoloModels.forEach(model => {
      completions.push(createCompletion(`yolo-${model}`, `yolo-${model}`, `YOLOv8 model`, WCDetailerCompletionTypeYolo));
    });
    
    return completions;
  }
  
  // Parse parameter block if present
  let paramBlock = '';
  let maskExpression = suffix;
  const paramMatch = suffix.match(/^(\[[^\]]*\])(.*)/);
  if (paramMatch) {
    paramBlock = paramMatch[1];
    maskExpression = paramMatch[2];
  }
  
  // If we're in the middle of typing a parameter block
  if (suffix.startsWith('[') && !suffix.includes(']')) {
    const paramContent = suffix.substring(1);
    const completions = [];
    
    // If we haven't typed anything in the param block yet
    if (paramContent === '') {
      completions.push(createCompletion('blur:', 'blur:', 'Override blur setting for this detailer', WCDetailerCompletionTypeParam));
      completions.push(createCompletion('creativity:', 'creativity:', 'Override creativity setting for this detailer', WCDetailerCompletionTypeParam));
    }
    // If we're in the middle of typing a parameter
    else {
      const lastCommaIndex = paramContent.lastIndexOf(',');
      const currentParam = lastCommaIndex >= 0 ? paramContent.substring(lastCommaIndex + 1).trim() : paramContent;
      
      // Check if we need to suggest parameter names
      if (!currentParam.includes(':')) {
        if (matchesPrefix('blur:', currentParam)) {
          completions.push(createCompletion('blur:', 'blur:', 'Override blur setting for this detailer', WCDetailerCompletionTypeParam));
        }
        if (matchesPrefix('creativity:', currentParam)) {
          completions.push(createCompletion('creativity:', 'creativity:', 'Override creativity setting for this detailer', WCDetailerCompletionTypeParam));
        }
      }
      // If we have a parameter name followed by colon but no value yet
      else if (currentParam.includes(':')) {
        const [paramName, paramValue] = currentParam.split(':', 2);
        const trimmedParamName = paramName.trim().toLowerCase();
        const trimmedParamValue = paramValue.trim();
        
        // If no value typed yet, show help text
        if (trimmedParamValue === '') {
          if (trimmedParamName === 'blur') {
            return ['\nEnter an integer value for blur (0-64). Amount of blur to apply to the detail mask before using it. Default is 10.'];
          } else if (trimmedParamName === 'creativity') {
            return ['\nEnter a decimal value for creativity (0.0-1.0). Controls how much the detailer changes the original content. Default is 0.5.'];
          }
        }
        // If we have a valid value, suggest next actions
        else {
          // Check if the current parameter has a valid value
          let hasValidValue = false;
          if (trimmedParamName === 'blur') {
            const blurValue = parseInt(trimmedParamValue);
            hasValidValue = !isNaN(blurValue) && blurValue >= 0 && blurValue <= 64;
          } else if (trimmedParamName === 'creativity') {
            const creativityValue = parseFloat(trimmedParamValue);
            hasValidValue = !isNaN(creativityValue) && creativityValue >= 0.0 && creativityValue <= 1.0;
          }
          
          if (hasValidValue) {
            const hasBlur = paramContent.includes('blur:');
            const hasCreativity = paramContent.includes('creativity:');
            
            if (!hasBlur && trimmedParamName !== 'blur') {
              completions.push(createCompletion(',blur:', ',blur:', 'Add blur parameter', WCDetailerCompletionTypeParam));
            }
            if (!hasCreativity && trimmedParamName !== 'creativity') {
              completions.push(createCompletion(',creativity:', ',creativity:', 'Add creativity parameter', WCDetailerCompletionTypeParam));
            }
            // Close the parameter block
            completions.push(createCompletion(']', ']', 'Close parameter block', WCDetailerCompletionTypeParam));
          }
        }
      }
    }
    
    return completions;
  }
  
  // Get available YOLO models
  const yoloModels = rawGenParamTypesFromServer.find(p => p.id == 'yolomodelinternal')?.values || [];
  
  // Available functions
  const functions = ['box', 'circle', 'hull', 'oval'];
  
  // If suffix is empty or just parameter block, show all initial options
  if (maskExpression === '') {
    const completions = [];
    
    // Add parameter block if not already present
    if (!paramBlock) {
      completions.push(createCompletion('[blur:', '[blur:', 'Override blur setting for this detailer', WCDetailerCompletionTypeParam));
      completions.push(createCompletion('[creativity:', '[creativity:', 'Override creativity setting for this detailer', WCDetailerCompletionTypeParam));
    }
    
    // Add functions
    functions.forEach(func => {
      completions.push(createCompletion(`${func}(`, `${func}(`, `Define a ${func}-shaped mask`, WCDetailerCompletionTypeFunction));
    });
    
    // Add YOLO models
    yoloModels.forEach(model => {
      completions.push(createCompletion(`yolo-${model}`, `yolo-${model}`, `YOLOv8 model`, WCDetailerCompletionTypeYolo));
    });
    
    return completions;
  }
  
  // Parse the current mask expression to understand context
  const completions = [];
  
  // Check if we're in the middle of typing a YOLO model
  const yoloMatch = maskExpression.match(/yolo-([^(\[\s|&+!)]*)$/);
  if (yoloMatch) {
    const partialModel = yoloMatch[1];
    
    // Check if we have an exact model match first
    const exactModel = yoloModels.find(m => m === partialModel);
    if (exactModel) {
      // We have an exact match, only suggest modifiers
      completions.push(createCompletion('(', '(', 'Add class filter', WCDetailerCompletionTypeNone));
      completions.push(createCompletion('[', '[', 'Add index selector', WCDetailerCompletionTypeNone));
      completions.push(createCompletion(' + ', ' + ', 'Grow mask by pixels', WCDetailerCompletionTypeNone));
    } else {
      // No exact match, suggest partial completions
      yoloModels.forEach(model => {
        if (matchesPrefix(model, partialModel)) {
          const remaining = model.substring(partialModel.length);
          if (remaining.length > 0) { // Only suggest if there are remaining characters
            completions.push(createCompletion(remaining, remaining, `YOLOv8 model: ${model}`, WCDetailerCompletionTypeYolo));
          }
        }
      });
    }
    
    return completions;
  }
  
  // Check if we're in the middle of typing a function
  const functionMatch = maskExpression.match(/\b([a-z]+)$/);
  if (functionMatch) {
    const partialFunction = functionMatch[1];
    functions.forEach(func => {
      if (matchesPrefix(func, partialFunction)) {
        const remaining = func.substring(partialFunction.length) + '(';
        completions.push(createCompletion(remaining, remaining, `Define a ${func}-shaped mask`, WCDetailerCompletionTypeFunction));
      }
    });
    
    // Don't return early here as it might also be a CLIPSEG term
  }
  
  // Check if we're at a position where we can add operators
  const canAddOperator = /[a-zA-Z0-9)\]]$/.test(maskExpression);
  if (canAddOperator) {
    // Add binary operators
    completions.push(createCompletion(' | ', ' | ', 'Union (OR) operation', WCDetailerCompletionTypeNone));
    completions.push(createCompletion(' & ', ' & ', 'Intersection (AND) operation', WCDetailerCompletionTypeNone));
    completions.push(createCompletion(' + ', ' + ', 'Grow mask by pixels', WCDetailerCompletionTypeNone));
    
    // Add index operator
    completions.push(createCompletion('[', '[', 'Select specific object by index', WCDetailerCompletionTypeNone));
    
    // Add threshold operators for CLIPSEG and YOLO
    if (!maskExpression.includes(':')) {
      completions.push(createCompletion(':', ':', 'Set threshold value', WCDetailerCompletionTypeNone));
    }
  }
  
  // Check if we're at a position where we can add unary operators or new terms
  const canAddUnary = /^$|[\s|&(]$/.test(maskExpression);
  if (canAddUnary) {
    // Use the helper function for mask specifier completions
    completions.push(...getMaskSpecifierCompletions());
  }
  
  // Check for help text scenarios for operators
  
  // Check if we're waiting for a grow amount after +
  const growMatch = maskExpression.match(/\+\s*(\d*)$/);
  if (growMatch) {
    const growValue = growMatch[1];
    if (growValue === '') {
      return ['\nEnter an integer value for grow pixels (0-512). Number of pixels to grow the detail mask by. Default is 16. Examples: 4, 8, 16, 32'];
    }
  }
  
  // Check if we're waiting for an index after [
  const indexMatch = maskExpression.match(/\[\s*(\d*)$/);
  if (indexMatch) {
    const indexValue = indexMatch[1];
    if (indexValue === '') {
      return ['\nEnter a 1-based index number to select which detected object to use. For example: [1] selects the first object, [2] selects the second object, etc.'];
    }
  }
  
  // Check if we're inside a function call and need parameter help
  const functionMatch2 = maskExpression.match(/(box|circle|oval|hull)\(([^)]*)$/);
  if (functionMatch2) {
    const functionName = functionMatch2[1];
    const paramContent = functionMatch2[2];
    const params = paramContent.split(',');
    const currentParamIndex = params.length - 1;
    const currentParam = params[currentParamIndex].trim();
    
    // If we just opened the function parentheses or are at a comma, show parameter help
    if (paramContent === '' || paramContent.endsWith(',') || currentParam === '') {
      switch (functionName) {
        case 'box':
          if (currentParamIndex === 0) {
            // For box, first parameter can be either x coordinate or mask specifier
            const helpText = [
                '\nEnter x coordinate (0.0-1.0). The x position as a percentage of image width. Example: 0.5 for center',
                '\nor enter a mask specifier to create a bounding box around the given mask',
            ];
            return [...helpText, ...getMaskSpecifierCompletions()];
          } else if (currentParamIndex === 1) {
            return ['\nEnter y coordinate (0.0-1.0). The y position as a percentage of image height. Example: 0.5 for center'];
          } else if (currentParamIndex === 2) {
            return ['\nEnter width (0.0-1.0). The width as a percentage of image width. Example: 0.3 for 30% width'];
          } else if (currentParamIndex === 3) {
            return ['\nEnter height (0.0-1.0). The height as a percentage of image height. Example: 0.2 for 20% height'];
          }
          break;
        case 'circle':
          if (currentParamIndex === 0) {
            // For circle, first parameter can be either x coordinate or mask specifier
            const helpText = [
                '\nEnter x coordinate (0.0-1.0). The x position of circle center as a percentage of image width. Example: 0.5 for center',
                '\nor enter a mask specifier to create a bounding circle around the given mask',
            ];
            return [...helpText, ...getMaskSpecifierCompletions()];
          } else if (currentParamIndex === 1) {
            return ['\nEnter y coordinate (0.0-1.0). The y position of circle center as a percentage of image height. Example: 0.5 for center'];
          } else if (currentParamIndex === 2) {
            return ['\nEnter radius (0.0-1.0). The radius as a percentage of smaller image dimension. Example: 0.2 for 20% radius'];
          }
          break;
        case 'oval':
          if (currentParamIndex === 0) {
            // For oval, first parameter can be either x coordinate or mask specifier
            const helpText = [
                '\nEnter x coordinate (0.0-1.0). The x position of oval center as a percentage of image width. Example: 0.5 for center',
                '\nor enter a mask specifier to create a bounding oval around the given mask',
            ];
            return [...helpText, ...getMaskSpecifierCompletions()];
          } else if (currentParamIndex === 1) {
            return ['\nEnter y coordinate (0.0-1.0). The y position of oval center as a percentage of image height. Example: 0.5 for center'];
          } else if (currentParamIndex === 2) {
            return ['\nEnter width (0.0-1.0). The width as a percentage of smaller image dimension. Example: 0.3 for 30% width'];
          } else if (currentParamIndex === 3) {
            return ['\nEnter height (0.0-1.0). The height as a percentage of smaller image dimension. Example: 0.2 for 20% height'];
          }
          break;
        case 'hull':
          if (currentParamIndex === 0) {
            // For hull, first parameter must be a mask specifier
            const helpText = ['\nEnter a mask specifier. The hull function creates a convex hull around the given mask. Example: face, yolo-person, etc.'];
            return [...helpText, ...getMaskSpecifierCompletions()];
          }
          break;
      }
    }
    
    // If we're inside a function call, don't suggest mask operations as they're invalid here
    return [];
  }
  
  // If we have no specific completions, provide general help
  if (completions.length === 0) {
    return [
      '\nSpecify before the ">" some text to match against in the image, like "<wcdetailer:face | hair>".',
      '\nCan also do "<wcdetailer:[blur:10,creativity:0.5]face | hair>".',
      '\nCan use operators: | (union), & (intersect), + (grow), ! (invert)',
      '\nCan use functions: box(), circle(), hull(), oval()',
      '\nCan use YOLO models with "yolo-" prefix',
      '\nSee https://github.com/bman654/SwarmUI-WildcardImporter/blob/main/wcdetailer.md for more details.'
    ];
  }
  
  return completions;
});
