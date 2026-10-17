/*
  Copyright (C) 2024 Bharath K A (bkumarbk9)
  SPDX-License-Identifier: AGPL-3.0-only

  This program is free software: you can redistribute it and/or modify
  it under the terms of the GNU Affero General Public License version 3
  as published by the Free Software Foundation.

  This program is distributed in the hope that it will be useful,
  but WITHOUT ANY WARRANTY; without even the implied warranty of
  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
  GNU Affero General Public License for more details.

  You should have received a copy of the GNU Affero General Public License
  along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/

var ws;
var thisCanvas;
var thisContext;
var offsetX;
var offsetY;
var mouseX;
var mouseY;
var prevMouseX;
var prevMouseY;
var mousePress;
var gPrevCache;

var rows;
var cols;
var rowSize;
var colSize;

var lineStrokeStyle;
var lineHilightStyle;
var lineWidth;
var lineHilightWidth;

var selectedCol;
var selectedRow;
var prevSelectedCol;
var prevSelectedRow;
var inputStart;
var itemText;
var hdrColor;
var hdrFont;

var textColor;
var textFont;

var txtItemSelected;
var maxChar;
var fileName;
var connected;

var statusLED;
var statusTxt;

function draw(data, txtColorArr, txtFontArr, hdrColor, hdrFont)
{


//thisContext.clearRect ( 0 , 0 , thisCanvas.width, thisCanvas.height );


    rows = data[2];
    cols = data[3];
    dataStart = inputStart;

    var element = thisCanvas;
    var dWidth = element.width;
    var dHeight = element.height;

    //console.log(, element.height);

    //if (dWidth == -1 || dHeight == -1)
    //{
    //    dWidth = element.width;
    //    dHeight = element.height;
    //    console.log("Setting element width and height");
    //}
    //else
    //{
    //    element.width = dWidth;
    //    element.height = dHeight;
    //}

    var ht = element.clientHeight;
    var width = element.clientWidth;
    var gctxt = element.getContext("2d");

    var grdnt = gctxt.createLinearGradient(0, 0, 0, ht);
    grdnt.addColorStop(0, "#EFF5FB");
    grdnt.addColorStop(0.1, "#FAFAFA");
    
    grdnt.addColorStop(1, "#F2F2F2");
    gctxt.fillStyle = grdnt;
    gctxt.fillRect(0, 0, dWidth, dHeight);

    gctxt.beginPath();
    gctxt.strokeStyle = lineStrokeStyle;
    gctxt.lineWidth = 3;
    gctxt.rect(0, 0, dWidth, dHeight);
  
    gctxt.shadowColor = "#999";
    gctxt.fill();
    gctxt.stroke();

    colSize = dWidth / cols;
    rowSize = dHeight / rows;
    for (var count = colSize; count < dWidth; count += colSize)
    {
        gctxt.beginPath();
        gctxt.strokeStyle = lineStrokeStyle;
        gctxt.lineWidth = lineWidth;
        gctxt.moveTo(count, 0);
        gctxt.lineTo(count, dHeight);
        gctxt.stroke();
    }

    for (var count = rowSize; count < dHeight; count += rowSize)
    {
        gctxt.beginPath();
        gctxt.strokeStyle = lineStrokeStyle;
        gctxt.lineWidth = lineWidth;
        gctxt.moveTo(0, count);
        gctxt.lineTo(dWidth, count);

        gctxt.stroke();
    }

    var colMid = colSize / 2;
    var rowMid = rowSize / 2;
    var fontCount = 0;
    gctxt.shadowColor = "#999";
    gctxt.shadowOffsetX = 0;
    gctxt.shadowOffsetY = 0;
    for (var rowCount = 0; rowCount < rows && dataStart < data.length; rowCount++)
    {
        for (var colCount = 0; colCount < cols && dataStart < data.length; colCount++)
        {
			itemText = data[dataStart];
            writeElement(colCount, rowCount);
            //if (rowCount == 0)
            //{
            //    gctxt.fillStyle = hdrColor;
            //    gctxt.font = hdrFont;
            //}
            //else
            //{
            //    gctxt.fillStyle = txtColorArr[fontCount];
            //    gctxt.font = txtFontArr[fontCount];
            //}

            //gctxt.textAlign = 'center';
            //gctxt.textBaseline = 'middle';
            //gctxt.fillText(data[dataStart], colCount + colMid, rowCount + rowMid);
            dataStart++;
            //fontCount++;
        }
    }

    //for (var rowCount = 0; rowCount < rows * rowSize && dataStart < data.length; rowCount += rowSize)
    //{
    //    for (var colCount = 0; colCount < cols * colSize && dataStart < data.length; colCount += colSize)
    //    {
    //        if (rowCount == 0)
    //        {
    //            gctxt.fillStyle = hdrColor;
    //            gctxt.font = hdrFont;
    //        }
    //        else
    //        {
    //            gctxt.fillStyle = txtColorArr[fontCount];
    //            gctxt.font = txtFontArr[fontCount];
    //        }
            
    //        gctxt.textAlign = 'center';
    //        gctxt.textBaseline = 'middle';
    //        gctxt.fillText(data[dataStart], colCount + colMid, rowCount + rowMid);
    //        dataStart++;
    //        fontCount++;
    //    }
    //}

    
}




function drawGrid(arr)
{
    var headerSize = inputStart;
    var txtColorArr = [];
    var txtFontArr = [];

    //for (var count = 0; count < arr.length; count++)
    //{
    //    if (Number(gPrevCache[count]) < Number(arr[headerSize + count]))
    //    {
    //        txtColorArr[count] = 'green';
    //        txtFontArr[count] = 'bold 12px Open Sans';
    //    }
    //    else if (Number(gPrevCache[count]) > Number(arr[headerSize + count]))
    //    {
    //        txtColorArr[count] = 'red';
    //        txtFontArr[count] = 'bold 12px Open Sans';
    //    }
    //    else
    //    {
    //        txtColorArr[count] = textColor;
    //        txtFontArr[count] = textFont;
    //    }
    //}
    draw(arr, txtColorArr, txtFontArr, hdrColor, hdrFont);

    //setTimeout(function ()
    //{
    //    for (var count = 0; count < arr.length; count++)
    //    {
    //        txtColorArr[count] = textColor;
    //        txtFontArr[count] = textFont;
    //        gPrevCache[count] = arr[headerSize + count];
    //    }
    //    draw(arr, txtColorArr, txtFontArr, hdrColor, hdrFont);
    //}, 500);

}

function clearSelectedArea()
{
    var topX = selectedCol * colSize;
    var topY = selectedRow * rowSize;
    thisContext.beginPath();
    thisContext.clearRect(topX + lineHilightWidth, topY + lineHilightWidth, colSize - lineHilightWidth, rowSize - lineHilightWidth);
    thisContext.stroke();
    thisContext.closePath();

}

function highlightArea() {
    var topX = Math.floor(mouseX / colSize) * colSize;
    var topY = Math.floor(mouseY / rowSize) * rowSize;


    thisContext.beginPath();
    thisContext.rect(topX, topY, colSize, rowSize);
    thisContext.lineWidth = lineHilightWidth;
    thisContext.strokeStyle = lineHilightStyle;
    thisContext.stroke();
    thisContext.closePath();

    if (prevMouseX != mouseX || prevMouseY != mouseY) {
        var newTopX = Math.floor(prevMouseX / colSize) * colSize;
        var newTopY = Math.floor(prevMouseY / rowSize) * rowSize;

        if (newTopX != topX || newTopY != topY) {
            thisContext.beginPath();
            thisContext.rect(newTopX, newTopY, colSize, rowSize);
            thisContext.lineWidth = lineWidth;
            thisContext.strokeStyle = lineStrokeStyle;
            thisContext.stroke();
            thisContext.closePath();
        }
    }

    prevMouseX = mouseX;
    prevMouseY = mouseY;
}

function writeElement(c, r)
{
	//clearSelectedArea();
    if (r == 0)
    {
        thisContext.fillStyle = hdrColor;
        thisContext.font = hdrFont;
    }
    else
    {
        thisContext.fillStyle =textColor;
        thisContext.font = textFont;
    }
    thisContext.textAlign = "center";
    thisContext.textBaseline = "middle";
    
    
    var txtToWrite = itemText;
    if (txtToWrite.length > maxChar) {
        txtToWrite = itemText.substr(0, maxChar);
        txtToWrite += ".."
    }
    
    thisContext.fillText(txtToWrite, c * colSize + colSize / 2, r * rowSize + rowSize / 2);
    
}

function actMouseMove(event) {
    var actingRect = thisCanvas.getBoundingClientRect();
    mouseX = event.x - offsetX;
    mouseY = event.y - offsetY;
    highlightArea();
}

function actMouseUp(event) {
    var actingRect = thisCanvas.getBoundingClientRect();
    mouseX = event.x - offsetX;
    mouseY = event.y - offsetY;

	if (selectedRow != -1)
	{
		-clearSelectedArea();
	}
    highlightArea();

    selectedCol = Math.floor(mouseX / colSize);
    selectedRow = Math.floor(mouseY / rowSize);

	if (prevSelectedCol != -1)
	{
		if (prevSelectedCol != selectedCol || prevSelectedRow != selectedRow) {
			itemText = txtItemSelected.value;
			gPrevCache[inputStart + prevSelectedRow * cols + prevSelectedCol] = itemText;
			txtItemSelected.value = "";
			console.log("Setting", gPrevCache[inputStart + prevSelectedRow * cols + prevSelectedCol]);

			writeElement(prevSelectedCol, prevSelectedRow);
			updateMsg();
		}
	}

    txtItemSelected.value = "";
    if (typeof gPrevCache[inputStart + selectedRow * cols + selectedCol] != 'undefined') {
        txtItemSelected.value = gPrevCache[inputStart + selectedRow * cols + selectedCol];
    }
    itemText = txtItemSelected.value;

    console.log("Retrieved ", itemText);

    //txtItemSelected.value = "";
	
    writeElement(selectedCol, selectedRow);

    prevSelectedCol = selectedCol;
    prevSelectedRow = selectedRow;
    
    txtItemSelected.focus();

    console.log("Selected col, row", selectedCol, selectedRow);
}

function actKBUp(event)
{
    console.log("key up", txtItemSelected.value.length, String.fromCharCode(event.charCode));
	
    
    clearSelectedArea();
    if (txtItemSelected.value.length > 0) {
        itemText = txtItemSelected.value + String.fromCharCode(event.charCode);
    }
    else {
        itemText = String.fromCharCode(event.charCode);
    }
	gPrevCache[inputStart + selectedRow * cols + selectedCol] = itemText;
    writeElement(selectedCol, selectedRow);
}

function regMouseEvents() {
    thisCanvas.addEventListener('mousemove', actMouseMove, false);
    thisCanvas.addEventListener('mouseup', actMouseUp, false);
    txtItemSelected.addEventListener('keyup', actKBUp);
}

function init() {
    thisCanvas = document.getElementById("testCanvas");
    thisContext = thisCanvas.getContext("2d");
	itemText = "";
	maxChar = 10;
	console.log(itemText.length);
    mouseX = 0;
    mouseY = 0;
    prevMouseX = 0;
    prevMouseY = 0;
    mousePress = false;
    gPrevCache = [0, 0, 0, 0];
    offsetX = thisCanvas.offsetLeft;
    offsetY = thisCanvas.offsetTop;
    lineStrokeStyle = "gray";
    lineHilightStyle = "blue";
    lineWidth = 2;
    lineHilightWidth = 1;
    selectedCol = -1;
    selectedRow = -1;
    inputStart = 4;
    prevSelectedCol = -1;
    prevSelectedRow = -1;
    hdrColor = 'black';
    hdrFont = 'bold 21px Times';
    textColor = 'black';
    textFont = '17px Open Sans';
    txtItemSelected = document.getElementById("selectedItem");
	txtItemSelected.style.visibility = "visible"
	regMouseEvents();
	fileName = document.getElementById("fileName").value;
	gPrevCache[1] = fileName.value;

	statusLED = document.getElementById("statusImg");
	statusTxt = document.getElementById("status");

    console.log("init Complete");

}

function updateMsg()
{
	gPrevCache[0] = "u";
	var sendBuff = gPrevCache.join();
    console.log("About to update data:", sendBuff.length);

    ws.send(sendBuff); 
    console.log("Update data sent:", sendBuff );
}

function saveMsg()
{
	gPrevCache[0] = "s";
    fileName = document.getElementById("fileName");
	gPrevCache[1] = fileName.value;
	console.log("saveMsg", gPrevCache[1] , fileName.value);

	var sendBuff = gPrevCache.join();
    console.log("About to send save data:", sendBuff.length);
	connected = 0;
    ws.send(sendBuff); 
    console.log("Save data sent:", sendBuff );
}

function openFile()
{
        console.log("About to send data");
		fileName = document.getElementById("fileName");

		var req = fileName.value;
		var sendReq = "Ready," + req;
        ws.send(sendReq); 
        console.log("Message sent!");
}

function closing()
{
        console.log("About to close");
        ws.send("##CLOSE##"); 
        console.log("Message sent!");
}

function statusDisconnect()
{
		statusLED.src = "redlight.gif"
		statusTxt.innerHTML  = "Not Connected";
}

function statusConnected()
{
		statusLED.src = "greenlight.gif"
		statusTxt.innerHTML  = "Connected";
}

function connect()
{
	if (typeof ws == 'undefined' || ws.readyState != 1)
	{	
		ws = new WebSocket("ws://localhost:8080/service");
		init();
		console.log("New Connection established");
	}
	else
	{
		console.log("Using established Connection");
		openFile();
	}

    ws.onopen = function()
    {
		openFile();
    };

    ws.onmessage = function(evt)
    {
        console.log("About to receive data");
        var received_msg = evt.data;
        console.log("Message received ", received_msg);

        var tempArr = [];
        var strArr = [];
		gPrevCache.length = 0;
        gPrevCache = received_msg.split(",");
        console.log("Size of Array = ", gPrevCache.length);
    
        drawGrid(gPrevCache);
		statusConnected();

        console.log("Message received ");
    };

    ws.onclose = function()
    {
		statusDisconnect();
        // websocket is closed.
        console.log("Connection is closed...");
    };
};

