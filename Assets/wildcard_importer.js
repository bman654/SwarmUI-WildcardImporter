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
    
    dropzone.textContent =`${files.length} files: ${files.map(file => file.name).join(", ")}`;
  }

  // Initialize destination folder and processing history
  updateDestinationFolder();
  updateHistory();
}

/**
 * Processes wildcard files by sending their Base64-encoded content to the backend.
 * @param {FileList | File[]} files - An array or FileList of File objects to process.
 */
async function processWildcards(files) {
  try {
    // Convert the FileList or Array of Files to an Array and map each to a FileData object
    const fileDataArray = await Promise.all(
      Array.from(files).map(async (file) => {
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
    const payload = { filesJson, name };

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
  } catch (error) {
    console.error("Error processing wildcards:", error);
    alert("An error occurred while processing the files. Please try again.");
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
          <p>Status: ${data.Status}</p>
          <p>Read Progress: ${data.InfilesProcessed} of ${data.Infiles} files examined</p>
          <p>Write Progress: ${data.OutfilesProcessed} of ${data.Outfiles} files written</p>
          <div class="ascii-progress">`;
    
    // Add the ASCII progress visualization with error handling
    try {
      statusContent += generateProgressMaze(data.InfilesProcessed, data.Infiles, data.OutfilesProcessed, data.Outfiles);
    } catch (error) {
      console.error("Error generating roguelike progress maze:", error);
      // Fallback to a simple progress display
      statusContent += `<div>Input files: ${data.InfilesProcessed}/${data.Infiles}, Output files: ${data.OutfilesProcessed}/${data.Outfiles}</div>`;
    }
    
    statusContent += `</div>`;
    statusDiv.innerHTML = statusContent;

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

    if (data.status !== "Completed") {
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

function updateHistory() {
  genericRequest("GetProcessingHistory", {}, (data) => {
    const historyDiv = document.getElementById(
      "wildcardImporter-processing-history"
    );
    historyDiv.innerHTML = "<h4>Processing History</h4>";
    if (data.history && data.history.length > 0) {
      const historyList = document.createElement("ul");
      data.history.forEach((item) => {
        const listItem = document.createElement("li");
        listItem.textContent = `${new Date(item.timestamp).toLocaleString()}: ${
          item.description
        }`;
        historyList.appendChild(listItem);
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

// Generates an ASCII art maze showing progress
function generateProgressMaze(infilesProcessed, totalInfiles, outfilesProcessed, totalOutfiles) {
  try {
    // Ensure all input parameters are valid numbers
    infilesProcessed = Number(infilesProcessed) || 0;
    totalInfiles = Number(totalInfiles) || 1; // Avoid division by zero
    outfilesProcessed = Number(outfilesProcessed) || 0;
    totalOutfiles = Number(totalOutfiles) || 0;
    
    // Determine which phase we're in - descending or ascending
    const descendingPhase = infilesProcessed < totalInfiles;
    
    // Calculate progress for current phase
    let phaseProgress;
    let currentDepth;
    let depthDescription;
    
    if (descendingPhase) {
      // Descending phase - going deeper into the dungeon
      phaseProgress = totalInfiles > 0 ? infilesProcessed / totalInfiles : 0;
      currentDepth = Math.max(1, Math.ceil(phaseProgress * 5)); // Depth 1-5, never below 1
      depthDescription = `Descending to Level ${currentDepth}`;
    } else {
      // Ascending phase - coming back up with the amulet
      phaseProgress = totalOutfiles > 0 ? outfilesProcessed / totalOutfiles : 0;
      currentDepth = Math.max(1, Math.floor(5 - (phaseProgress * 4))); // Depth 5-1, never below 1
      depthDescription = `Ascending from Level ${currentDepth}`;
    }
    
    // Characters for our roguelike dungeon
    const wall = '#';            // Wall
    const wallVert = '|';        // Vertical wall
    const corner = '+';          // Corner/door
    const player = '@';          // Player character
    const start = '<';           // Stairs up
    const end = ',';             // Amulet of Yendor (traditionally comma in Rogue)
    const space = '.';           // Floor
    const treasures = ['$', '*', '8']; // Gold, gems, valuable items
    const stairs = '>'; // Stairs down
    
    // Monster types (by depth level)
    const monsters = [
      ['k', 'r', 'B'],           // Level 1: kobolds, rats, bats
      ['g', 'o', 'S'],           // Level 2: goblins, orcs, snakes
      ['T', 'O', 'Z'],           // Level 3: trolls, ogres, zombies
      ['D', 'V', 'W'],           // Level 4: dragons, vampires, wraiths
      ['&', 'L', 'J']            // Level 5: demons, liches, jabberwocks
    ];
    
    // Width of the progress bar (excluding borders)
    const width = 50;
    
    // Calculate player position based on phase
    let playerPos;
    if (descendingPhase) {
      // Moving left to right while descending
      playerPos = Math.floor(phaseProgress * width);
    } else {
      // Moving right to left while ascending
      playerPos = Math.floor((1 - phaseProgress) * width);
    }
    
    // Player has the amulet during ascent
    const playerChar = descendingPhase ? player : player + end;
    
    // Generate the 5 lines of the dungeon
    const maze = [];
    
    // Line 1: Top border with stairs and dungeon edge
    let line1 = `  <span class="rogue-special">${start}</span> `;
    line1 += `<span class="rogue-wall">${wall.repeat(width)}</span>`;
    line1 += ` <span class="rogue-special">${stairs}</span>`;
    maze.push(line1);
    
    // Line 2: The main dungeon path with player
    let line2 = `  <span class="rogue-wall">${wallVert}</span> `;
    for (let i = 0; i < width; i++) {
      if (i === playerPos) {
        if (descendingPhase) {
          line2 += `<span class="rogue-player">${player}</span>`;
        } else {
          // During ascent, player carries the amulet
          line2 += `<span class="rogue-player rogue-special">${player}</span>`;
        }
      } else if ((descendingPhase && i < playerPos) || (!descendingPhase && i > playerPos)) {
        // Add occasional treasure in cleared areas
        if (i % 7 === 0 && i % 3 === 0) {
          line2 += `<span class="rogue-item">${treasures[i % treasures.length]}</span>`;
        } else {
          line2 += `<span class="rogue-floor">${space}</span>`;
        }
      } else {
        // Add monsters based on current depth
        if (i % (7 - currentDepth) === 0) {
          // Select monster based on current dungeon depth (ensure index is valid)
          const monsterIndex = Math.max(0, Math.min(currentDepth - 1, 4)); // Ensure index is between 0-4
          const monsterSet = monsters[monsterIndex];
          if (monsterSet && monsterSet.length > 0) {
            const monsterChar = monsterSet[i % monsterSet.length];
            line2 += `<span class="rogue-monster-${Math.min(currentDepth, 5)}">${monsterChar}</span>`;
          } else {
            line2 += `<span class="rogue-floor">${space}</span>`;
          }
        } else {
          line2 += `<span class="rogue-floor">${space}</span>`;
        }
      }
    }
    line2 += ` <span class="wall">${wallVert}</span>`;
    maze.push(line2);
    
    // Line 3: Middle dungeon corridor with monsters
    let line3 = `  <span class="rogue-wall">${wallVert}</span> `;
    for (let i = 0; i < width; i++) {
      if (currentDepth > 1 && i % (8 - currentDepth) === 3) {
        // Select monster based on current depth (ensure index is valid)
        const monsterIndex = Math.max(0, Math.min(currentDepth - 1, 4)); // Ensure index is between 0-4
        const monsterSet = monsters[monsterIndex];
        if (monsterSet && monsterSet.length > 0) {
          const monsterChar = monsterSet[(i + 1) % monsterSet.length];
          line3 += `<span class="rogue-monster-${Math.min(currentDepth, 5)}">${monsterChar}</span>`;
        } else {
          line3 += `<span class="rogue-floor">${space}</span>`;
        }
      } else if (i % 9 === 0) {
        if (descendingPhase && i > width * 0.8 && infilesProcessed > totalInfiles * 0.8) {
          // Show amulet near the end of descent
          line3 += `<span class="rogue-special">${end}</span>`;
        } else {
          // Add occasional treasure
          line3 += `<span class="rogue-item">${treasures[(i/9) % treasures.length]}</span>`;
        }
      } else {
        line3 += `<span class="rogue-floor">${space}</span>`;
      }
    }
    line3 += ` <span class="wall">${wallVert}</span>`;
    maze.push(line3);
    
    // Line 4: Bottom dungeon corridor with monsters and items
    let line4 = `  <span class="rogue-wall">${wallVert}</span> `;
    for (let i = 0; i < width; i++) {
      if (currentDepth > 2 && i % (9 - currentDepth) === 5) {
        // Harder monsters for higher depth (ensure index is valid)
        const monsterIndex = Math.max(0, Math.min(currentDepth - 1, 4)); // Ensure index is between 0-4
        const monsterSet = monsters[monsterIndex];
        if (monsterSet && monsterSet.length > 0) {
          const monsterChar = monsterSet[(i + 2) % monsterSet.length];
          line4 += `<span class="rogue-monster-${Math.min(currentDepth, 5)}">${monsterChar}</span>`;
        } else {
          line4 += `<span class="rogue-floor">${space}</span>`;
        }
      } else if (!descendingPhase && i === playerPos + 3) {
        // Special item near the player during ascent
        line4 += '<span class="rogue-special">!</span>'; // Potion
      } else if (i % 11 === 0) {
        // Occasional feature
        const feature = ['^', '!', '=', '%'][i % 4]; // Trap, potion, ring, food
        line4 += `<span class="rogue-special">${feature}</span>`;
      } else {
        line4 += `<span class="rogue-floor">${space}</span>`;
      }
    }
    line4 += ` <span class="wall">${wallVert}</span>`;
    maze.push(line4);
    
    // Line 5: Display depth and phase info
    let line5;
    if (descendingPhase) {
      // Show stairs down during descent
      line5 = `  <span class="rogue-wall">${corner}</span> `;
      line5 += `<span class="rogue-wall">${wall.repeat(playerPos)}</span>`;
      if (playerPos < width) {
        line5 += `<span class="rogue-wall">${wall.repeat(width - playerPos)}</span>`;
      }
      line5 += ` <span class="rogue-special">${stairs}</span>`;
    } else {
      // Show stairs up during ascent
      line5 = `  <span class="rogue-special">${start}</span> `;
      line5 += `<span class="rogue-wall">${wall.repeat(width)}</span>`;
      line5 += ` <span class="rogue-wall">${corner}</span>`;
    }
    maze.push(line5);
    
    // Stats line showing progress in roguelike style
    let phaseProgressPercent = Math.round(phaseProgress * 100);
    let overallProgressPercent = Math.round((infilesProcessed + outfilesProcessed) / (totalInfiles + totalOutfiles) * 100);
    
    // Calculate HP based on current phase
    let hpPercent = descendingPhase ? phaseProgress : (1 - phaseProgress);
    let hp = '@'.repeat(Math.ceil(hpPercent * 10));
    
    let statsLine = `  ${depthDescription} | `;
    
    if (descendingPhase) {
      if (infilesProcessed >= totalInfiles) {
        statsLine += `Found Amulet! | `;
      } else {
        statsLine += `Searching: ${phaseProgressPercent}% | `;
      }
      statsLine += `Read: ${infilesProcessed}/${totalInfiles} | Target Files: ${totalOutfiles}`;
    } else {
      statsLine += `Escaping: ${phaseProgressPercent}% | Written: ${outfilesProcessed}/${totalOutfiles}`;
      if (outfilesProcessed >= totalOutfiles) {
        statsLine += ` | ESCAPED!`;
      }
    }
    
    statsLine += ` | HP: <span class="rogue-player">${hp}</span>`;
    maze.push(statsLine);
    
    return maze.join('\n');
  } catch (error) {
    console.error("Error in generateProgressMaze:", error);
    // Return a simple fallback progress display
    return `<div>Progress: Input files: ${infilesProcessed}/${totalInfiles}, Output files: ${outfilesProcessed}/${totalOutfiles}</div>`;
  }
}
