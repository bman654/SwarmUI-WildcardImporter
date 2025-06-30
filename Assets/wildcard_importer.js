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
