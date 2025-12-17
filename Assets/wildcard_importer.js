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

const WCDetailerConfig = {
  parameters: {
    blur: { 
      type: 'int', 
      min: 0, 
      max: 64, 
      default: 10, 
      desc: 'Amount of blur to apply to the detail mask before using it. Default is 10.' 
    },
    creativity: { 
      type: 'float', 
      min: 0.0, 
      max: 1.0, 
      default: 0.5, 
      desc: 'Controls how much the detailer changes the original content. Default is 0.5.' 
    }
  },
  
  functions: {
    box: {
      params: [
        { name: 'x_or_mask', type: 'coord_or_mask', desc: 'The x position as a percentage of image width. Example: 0.5 for center', altDesc: 'or enter a mask specifier to create a bounding box around the given mask' },
        { name: 'y', type: 'coord', desc: 'The y position as a percentage of image height. Example: 0.5 for center' },
        { name: 'width', type: 'coord', desc: 'The width as a percentage of image width. Example: 0.3 for 30% width' },
        { name: 'height', type: 'coord', desc: 'The height as a percentage of image height. Example: 0.2 for 20% height' }
      ],
      desc: 'Define a box-shaped mask'
    },
    circle: {
      params: [
        { name: 'x_or_mask', type: 'coord_or_mask', desc: 'The x position of circle center as a percentage of image width. Example: 0.5 for center', altDesc: 'or enter a mask specifier to create a bounding circle around the given mask' },
        { name: 'y', type: 'coord', desc: 'The y position of circle center as a percentage of image height. Example: 0.5 for center' },
        { name: 'radius', type: 'coord', desc: 'The radius as a percentage of smaller image dimension. Example: 0.2 for 20% radius' }
      ],
      desc: 'Define a circle-shaped mask'
    },
    oval: {
      params: [
        { name: 'x_or_mask', type: 'coord_or_mask', desc: 'The x position of oval center as a percentage of image width. Example: 0.5 for center', altDesc: 'or enter a mask specifier to create a bounding oval around the given mask' },
        { name: 'y', type: 'coord', desc: 'The y position of oval center as a percentage of image height. Example: 0.5 for center' },
        { name: 'width', type: 'coord', desc: 'The width as a percentage of smaller image dimension. Example: 0.3 for 30% width' },
        { name: 'height', type: 'coord', desc: 'The height as a percentage of smaller image dimension. Example: 0.2 for 20% height' }
      ],
      desc: 'Define a oval-shaped mask'
    },
    hull: {
      params: [
        { name: 'mask', type: 'mask_only', desc: 'The hull function creates a convex hull around the given mask. Example: face, yolo-person, etc.' }
      ],
      desc: 'Define a hull-shaped mask'
    }
  },
  
  operators: {
    binary: [
      { symbol: ' | ', desc: 'Union (OR) operation', context: 'after_term' },
      { symbol: ' & ', desc: 'Intersection (AND) operation', context: 'after_term' },
      { symbol: ' + ', desc: 'Grow mask by pixels', context: 'after_term', helpPattern: /\+\s*(\d*)$/, helpText: 'Enter an integer value for grow pixels (0-512). Number of pixels to grow the detail mask by. Default is 16. Examples: 4, 8, 16, 32' }
    ],
    unary: [
      { symbol: '!', desc: 'Invert (NOT) operation', context: 'start_or_after_operator' },
      { symbol: '(', desc: 'Group expressions', context: 'start_or_after_operator' }
    ],
    postfix: [
      { symbol: '[', desc: 'Select specific object by index', context: 'after_term', helpPattern: /\[\s*(\d*)$/, helpText: 'Enter a 1-based index number to select which detected object to use. For example: [1] selects the first object, [2] selects the second object, etc.' },
      { symbol: ':', desc: 'Set threshold value', context: 'after_term', condition: (expr) => !expr.includes(':') }
    ]
  },
  
  contexts: [
    { name: 'parameter_block', pattern: /^\[([^\]]*?)$/, priority: 1 },
    { name: 'yolo_partial', pattern: /yolo-([^(\[\s|&+!)]*)$/, priority: 2 },
    { name: 'function_call', pattern: /(box|circle|oval|hull)\(([^)]*)$/, priority: 3 },
    { name: 'function_partial', pattern: /\b([a-z]+)$/, priority: 4 },
    { name: 'operator_help', pattern: /(\+\s*\d*|\[\s*\d*)$/, priority: 5 },
    { name: 'empty_expression', pattern: /^$/, priority: 6 },
    { name: 'after_term', pattern: /[a-zA-Z0-9)\]]$/, priority: 7 },
    { name: 'start_or_after_operator', pattern: /^$|[\s|&(]$/, priority: 8 }
  ]
};

class WCDetailerCompletionEngine {
  constructor(config) {
    this.config = config;
    this.yoloModelsCache = null;
  }
  
  getYoloModels() {
    if (!this.yoloModelsCache) {
      this.yoloModelsCache = rawGenParamTypesFromServer.find(p => p.id == 'yolomodelinternal')?.values || [];
    }
    return this.yoloModelsCache;
  }
  
  createCompletion(completionText, displayText, description, type, prompt) {
    const index = promptTabComplete.findLastWordIndex(prompt);
    return new WCDetailerCompletionItem(`${prompt.substring(index)}${completionText}`, displayText, description, type);
  }
  
  matchesPrefix(text, prefix) {
    return text.toLowerCase().startsWith(prefix.toLowerCase());
  }
  
  parseCurrentState(suffix) {
    const state = {
      original: suffix,
      paramBlock: '',
      maskExpression: suffix
    };
    
    // Parse parameter block if present
    const paramMatch = suffix.match(/^(\[[^\]]*\])(.*)/);
    if (paramMatch) {
      state.paramBlock = paramMatch[1];
      state.maskExpression = paramMatch[2];
    }
    
    return state;
  }
  
  detectContext(state) {
    const expr = state.maskExpression;
    
    // Check parameter block context first
    if (state.original.startsWith('[') && !state.original.includes(']')) {
      return 'parameter_block';
    }
    
    // Check other contexts in priority order
    for (const context of this.config.contexts) {
      if (context.pattern.test(expr)) {
        return context.name;
      }
    }
    
    return 'default';
  }
  
  handleParameterBlock(state, prompt) {
    const paramContent = state.original.substring(1);
    const completions = [];
    
    if (paramContent === '') {
      // Suggest initial parameters
      for (const [name, param] of Object.entries(this.config.parameters)) {
        completions.push(this.createCompletion(`[${name}:`, `[${name}:`, `Override ${name} setting for this detailer`, WCDetailerCompletionTypeParam, prompt));
      }
    } else {
      const lastCommaIndex = paramContent.lastIndexOf(',');
      const currentParam = lastCommaIndex >= 0 ? paramContent.substring(lastCommaIndex + 1).trim() : paramContent;
      
      if (!currentParam.includes(':')) {
        // Suggest parameter names
        for (const [name, param] of Object.entries(this.config.parameters)) {
          if (this.matchesPrefix(`${name}:`, currentParam)) {
            completions.push(this.createCompletion(`${name}:`, `${name}:`, `Override ${name} setting for this detailer`, WCDetailerCompletionTypeParam, prompt));
          }
        }
      } else {
        const [paramName, paramValue] = currentParam.split(':', 2);
        const trimmedParamName = paramName.trim().toLowerCase();
        const trimmedParamValue = paramValue.trim();
        const paramConfig = this.config.parameters[trimmedParamName];
        
        if (paramConfig && trimmedParamValue === '') {
          return [`\nEnter a ${paramConfig.type === 'int' ? 'integer' : 'decimal'} value for ${trimmedParamName} (${paramConfig.min}-${paramConfig.max}). ${paramConfig.desc}`];
        } else if (paramConfig && this.isValidParamValue(trimmedParamValue, paramConfig)) {
          // Suggest next actions
          const hasParams = Object.keys(this.config.parameters).reduce((acc, name) => {
            acc[name] = paramContent.includes(`${name}:`);
            return acc;
          }, {});
          
          for (const [name, param] of Object.entries(this.config.parameters)) {
            if (!hasParams[name] && trimmedParamName !== name) {
              completions.push(this.createCompletion(`,${name}:`, `,${name}:`, `Add ${name} parameter`, WCDetailerCompletionTypeParam, prompt));
            }
          }
          completions.push(this.createCompletion(']', ']', 'Close parameter block', WCDetailerCompletionTypeParam, prompt));
        }
      }
    }
    
    return completions;
  }
  
  isValidParamValue(value, paramConfig) {
    if (paramConfig.type === 'int') {
      const intValue = parseInt(value);
      return !isNaN(intValue) && intValue >= paramConfig.min && intValue <= paramConfig.max;
    } else if (paramConfig.type === 'float') {
      const floatValue = parseFloat(value);
      return !isNaN(floatValue) && floatValue >= paramConfig.min && floatValue <= paramConfig.max;
    }
    return false;
  }
  
  handleYoloCompletion(state, prompt) {
    const match = state.maskExpression.match(/yolo-([^(\[\s|&+!)]*)$/);
    if (!match) return [];
    
    const partialModel = match[1];
    const yoloModels = this.getYoloModels();
    const completions = [];
    
    // Check for exact match first
    const exactModel = yoloModels.find(m => m === partialModel);
    if (exactModel) {
      completions.push(this.createCompletion('(', '(', 'Add class filter', WCDetailerCompletionTypeNone, prompt));
      completions.push(this.createCompletion('[', '[', 'Add index selector', WCDetailerCompletionTypeNone, prompt));
      completions.push(this.createCompletion(' + ', ' + ', 'Grow mask by pixels', WCDetailerCompletionTypeNone, prompt));
    } else {
      // No exact match, suggest partial completions
      yoloModels.forEach(model => {
        if (this.matchesPrefix(model, partialModel)) {
          const remaining = model.substring(partialModel.length);
          if (remaining.length > 0) {
            completions.push(this.createCompletion(remaining, remaining, `YOLOv8 model: ${model}`, WCDetailerCompletionTypeYolo, prompt));
          }
        }
      });
    }
    
    return completions;
  }
  
  handleFunctionCall(state, prompt) {
    const match = state.maskExpression.match(/(box|circle|oval|hull)\(([^)]*)$/);
    if (!match) return [];
    
    const functionName = match[1];
    const paramContent = match[2];
    const params = paramContent.split(',');
    const currentParamIndex = params.length - 1;
    const currentParam = params[currentParamIndex].trim();
    const functionConfig = this.config.functions[functionName];
    
    if (!functionConfig) return [];
    
    // If we just opened parentheses or are at a comma, show parameter help
    if (paramContent === '' || paramContent.endsWith(',') || currentParam === '') {
      const paramConfig = functionConfig.params[currentParamIndex];
      if (!paramConfig) return [];
      
      const helpText = [`\nEnter ${paramConfig.desc}`];
      
      if (paramConfig.type === 'coord_or_mask' || paramConfig.type === 'mask_only') {
        if (paramConfig.altDesc) {
          helpText.push(`\n${paramConfig.altDesc}`);
        }
        return [...helpText, ...this.getMaskSpecifierCompletions(prompt)];
      }
      
      return helpText;
    }
    
    return [];
  }
  
  handleFunctionPartial(state, prompt) {
    const match = state.maskExpression.match(/\b([a-z]+)$/);
    if (!match) return [];
    
    const partialFunction = match[1];
    const completions = [];
    
    // Add matching function completions
    for (const [funcName, funcConfig] of Object.entries(this.config.functions)) {
      if (this.matchesPrefix(funcName, partialFunction)) {
        const remaining = funcName.substring(partialFunction.length) + '(';
        completions.push(this.createCompletion(remaining, remaining, funcConfig.desc, WCDetailerCompletionTypeFunction, prompt));
      }
    }
    
    // Always add operators for partial terms (matching original behavior)
    const operatorCompletions = this.handleAfterTerm(state, prompt);
    completions.push(...operatorCompletions);
    
    return completions;
  }
  
  handleYoloPartial(state, prompt) {
    const match = state.maskExpression.match(/yolo-([^(\[\s|&+!)]*)$/);
    if (!match) return [];
    
    const partialModel = match[1];
    const completions = [];
    const yoloModels = this.getYoloModels();
    
    for (const model of yoloModels) {
      if (this.matchesPrefix(model, partialModel)) {
        const remaining = model.substring(partialModel.length);
        completions.push(this.createCompletion(remaining, remaining, `YOLOv8 model: ${model}`, WCDetailerCompletionTypeYolo, prompt));
      }
    }
    
    return completions;
  }
  
  handleOperatorHelp(state, prompt) {
    const expr = state.maskExpression;
    
    // Check for operator help patterns
    for (const operator of this.config.operators.binary) {
      if (operator.helpPattern && operator.helpPattern.test(expr)) {
        return [operator.helpText];
      }
    }
    
    for (const operator of this.config.operators.postfix) {
      if (operator.helpPattern && operator.helpPattern.test(expr)) {
        return [operator.helpText];
      }
    }
    
    return [];
  }
  
  handleEmptyExpression(state, prompt) {
    const completions = [];
    
    // Add parameter block if not already present
    if (!state.paramBlock) {
      for (const [name, param] of Object.entries(this.config.parameters)) {
        completions.push(this.createCompletion(`[${name}:`, `[${name}:`, `Override ${name} setting for this detailer`, WCDetailerCompletionTypeParam, prompt));
      }
    }
    
    // Add mask specifier completions
    completions.push(...this.getMaskSpecifierCompletions(prompt));
    
    return completions;
  }
  
  handleAfterTerm(state, prompt) {
    const completions = [];
    
    // Add binary operators
    for (const operator of this.config.operators.binary) {
      completions.push(this.createCompletion(operator.symbol, operator.symbol, operator.desc, WCDetailerCompletionTypeNone, prompt));
    }
    
    // Add postfix operators
    for (const operator of this.config.operators.postfix) {
      if (!operator.condition || operator.condition(state.maskExpression)) {
        completions.push(this.createCompletion(operator.symbol, operator.symbol, operator.desc, WCDetailerCompletionTypeNone, prompt));
      }
    }
    
    return completions;
  }
  
  handleStartOrAfterOperator(state, prompt) {
    return this.getMaskSpecifierCompletions(prompt);
  }
  
  getMaskSpecifierCompletions(prompt) {
    const completions = [];
    
    // Add unary operators
    for (const operator of this.config.operators.unary) {
      completions.push(this.createCompletion(operator.symbol, operator.symbol, operator.desc, WCDetailerCompletionTypeNone, prompt));
    }
    
    // Add functions
    for (const [funcName, funcConfig] of Object.entries(this.config.functions)) {
      completions.push(this.createCompletion(`${funcName}(`, `${funcName}(`, funcConfig.desc, WCDetailerCompletionTypeFunction, prompt));
    }
    
    // Add YOLO models
    const yoloModels = this.getYoloModels();
    yoloModels.forEach(model => {
      completions.push(this.createCompletion(`yolo-${model}`, `yolo-${model}`, `YOLOv8 model`, WCDetailerCompletionTypeYolo, prompt));
    });
    
    return completions;
  }
  
  getCompletions(suffix, prompt) {
    const state = this.parseCurrentState(suffix.trim());
    const context = this.detectContext(state);
    
    // Call appropriate handler
    const handlerName = `handle${context.charAt(0).toUpperCase() + context.slice(1).replace(/_([a-z])/g, (_, letter) => letter.toUpperCase())}`;
    
    if (typeof this[handlerName] === 'function') {
      const result = this[handlerName](state, prompt);
      if (result && result.length > 0) {
        return result;
      }
    }
    
    // Default help text
    return [
      '\nSpecify before the ">" some text to match against in the image, like "<wcdetailer:face | hair>".',
      '\nCan also do "<wcdetailer:[blur:10,creativity:0.5]face | hair>".',
      '\nCan use operators: | (union), & (intersect), + (grow), ! (invert)',
      '\nCan use functions: box(), circle(), hull(), oval()',
      '\nCan use YOLO models with "yolo-" prefix',
      '\nSee https://github.com/bman654/SwarmUI-WildcardImporter/blob/main/wcdetailer.md for more details.'
    ];
  }
}

// Create the new completion engine instance
const wcDetailerEngine = new WCDetailerCompletionEngine(WCDetailerConfig);

promptTabComplete.registerPrefix('wcdetailer', 'Automatically segment an area by CLIP matcher and set operations and inpaint it (optionally with a unique prompt)', (suffix, prompt) => {
  return wcDetailerEngine.getCompletions(suffix, prompt);
});

// End of WCDetailer completion system

promptTabComplete.registerPrefix('wcrandom', 'Selects from a set of random words to include', (prefix) => {
    return [
        '\nSpecify a comma-separated list of words to choose from, like "<wcrandom:cat,dog,elephant>".', 
        '\nYou can use "||" instead of "," if you need to include commas in your values.'
    ];
});
promptTabComplete.registerPrefix('wcrandom[2-4]', 'Selects multiple options from a set of random words to include', (prefix) => {
    return [
        '\nSpecify a comma-separated list of words to choose from, like "<wcrandom[2]:cat,dog,elephant>".', 
        '\nYou can use "||" instead of "," if you need to include commas in your values.', 
        '\nPut a comma in the input (eg "<wcrandom[2,]:red,green,blue>") to separate the results with commas.',
        '\nOr use a custom separator by placing something after the comma (eg "<wcrandom[2, and ]:red,green,blue>").'
    ];
});

promptTabComplete.registerPrefix('wcnegative', 'Appends text to the negative prompt', (prefix) => {
    return [
        '\nAppends text to the negative prompt, like "<wcnegative:blurry, low quality>".',
        '\nUse "[prepend]" to add to the beginning: "<wcnegative[prepend]:worst quality, >".'
    ];
});

promptTabComplete.registerPrefix('wcnegative[prepend]', 'Prepends text to the negative prompt', (prefix) => {
    return [
        '\nPrepends text to the negative prompt, like "<wcnegative[prepend]:worst quality, >".'
    ];
});

promptTabComplete.registerPrefix('wcaddvar[var_name]', 'Appends or prepends content to an existing variable', (prefix) => {
    return [
        '\nModifies existing variables created with <setvar>.',
        '\nSyntax: "<wcaddvar[variable_name]:content to add>"',
        '\nUse prepend mode: "<wcaddvar[variable_name,prepend]:content to prepend>"',
        '\nCreates the variable if it doesn\'t exist.'
    ];
});

promptTabComplete.registerPrefix('wcaddmacro[macro_name]', 'Appends or prepends content to an existing macro', (prefix) => {
    return [
        '\nModifies existing macros created with <setmacro>.',
        '\nSyntax: "<wcaddmacro[macro_name]:content to add>"',
        '\nUse prepend mode: "<wcaddmacro[macro_name,prepend]:content to prepend>"',
        '\nCreates the macro if it doesn\'t exist.'
    ];
});

promptTabComplete.registerPrefix('wcpushvar[var_name]', 'Saves current variable value to stack and sets new value', (prefix) => {
    return [
        '\nProvides stack-based variable management for temporary modifications.',
        '\nSyntax: "<wcpushvar[variable_name]:new_value>"',
        '\nUse with <wcpopvar:variable_name> to restore the previous value.',
        '\nUseful for temporary variable changes within nested contexts.'
    ];
});

promptTabComplete.registerPrefix('wcpopvar', 'Restores previous variable value from stack', (prefix) => {
    return [
        '\nRestores the previous value of a variable from its stack.',
        '\nSyntax: "<wcpopvar:variable_name>"',
        '\nUse after <wcpushvar> to restore the original value.',
        '\nSets variable to empty string if stack is empty.'
    ];
});

promptTabComplete.registerPrefix('wcpushmacro[macro_name]', 'Saves current macro value to stack and sets new value', (prefix) => {
    return [
        '\nProvides stack-based macro management for temporary modifications.',
        '\nSyntax: "<wcpushmacro[macro_name]:new_value>"',
        '\nUse with <wcpopmacro:macro_name> to restore the previous value.',
        '\nUseful for temporary macro changes within nested contexts.'
    ];
});

promptTabComplete.registerPrefix('wcpopmacro', 'Restores previous macro value from stack', (prefix) => {
    return [
        '\nRestores the previous value of a macro from its stack.',
        '\nSyntax: "<wcpopmacro:macro_name>"',
        '\nUse after <wcpushmacro> to restore the original value.',
        '\nSets macro to empty string if stack is empty.'
    ];
});

promptTabComplete.registerPrefix('wcmatch', 'Provides conditional logic for prompts using expression evaluation', (prefix) => {
    return [
        '\nUse with <wccase> blocks to create conditional content.',
        '\nExample: "<wcmatch:<wccase[myvar eq \"value\"]:content if true><wccase:default content>>"',
        '\nSupports variable comparisons, logical operators (and, or, not), and string functions.',
        '\nOnly the first matching case will be rendered.'
    ];
});

promptTabComplete.registerPrefix('wccase[var_name eq "value"]', 'Conditional case block used within <wcmatch>', (prefix) => {
    return [
        '\nMust be used inside a <wcmatch> block.',
        '\nWith condition: "<wccase[condition]:content if condition is true>"',
        '\nDefault case: "<wccase:content if no other cases match>"',
        '\nSupports expressions like: myvar eq "value", contains(myvar, "text"), length(myvar) > 5'
    ];
});


promptTabComplete.registerPrefix('wcwildcard', 'Select a random line from a wildcard file (presaved list of options) (works same as "wcrandom" but for wildcards)', (prefix) => {
    let prefixLow = prefix.toLowerCase();
    return promptTabComplete.getOrderedMatches(wildcardHelpers.allWildcards, prefixLow);
});

promptTabComplete.registerPrefix('wcwildcard[2-4]', 'Select multiple random lines from a wildcard file (presaved list of options) (works same as "wcrandom" but for wildcards)', (prefix) => {
    let prefixLow = prefix.toLowerCase();
    return promptTabComplete.getOrderedMatches(wildcardHelpers.allWildcards, prefixLow);
});
