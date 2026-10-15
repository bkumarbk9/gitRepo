/*
  Copyright (C) Bharath K A

  THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT
  NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND 
  NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, 
  DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, 
  OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE. 
*/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using System.IO;

namespace wsSocket
{
    internal class socketState
    {
        public long sockId;
        public Socket sockClient;
        public long bytesReceived = 0;
    }

  public class html5Stream
  {
    private Socket serverSocket;
    private string uniqueID = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
    private SHA1 sha1Crypto = SHA1CryptoServiceProvider.Create();
    private object lockObj = new object();

    private AutoResetEvent eventDataAvailable = new AutoResetEvent(false);
    private bool shutDown = false;
    private Dictionary<long, Socket> socketsToUpdate = new Dictionary<long, Socket>();
    private fastParser fastPrsr = new fastParser();

    private Dictionary<long, byte[]> clientList = new Dictionary<long,byte[]>();
    private Dictionary<long, StringBuilder> currClientData = new Dictionary<long, StringBuilder>();
    private HashSet<long> sockIdList = new HashSet<long>();
    private HashSet<long> updatesAvailable = new HashSet<long>();
    private long maxBufferSize = 204800;
    private string initData = "-1, -1, 45,22";


    public long getUniqueId()
    {
        long retVal; 
        lock(lockObj)
        {
            retVal = sockIdList.First();
            sockIdList.Remove(retVal);
        }

        return retVal;

    }

    private void returnUniqueId(long uniqueId)
    {
        lock(lockObj)
        {
            sockIdList.Add(uniqueId);
        }
    }

    private void cleanupUniqueId(long uniqueId)
    {
        lock (lockObj)
        {
            returnUniqueId(uniqueId);
            clientList.Remove(uniqueId);
            socketsToUpdate.Remove(uniqueId);
            currClientData.Remove(uniqueId);
        }
    }

    public void startServer(string endPt = "", bool demo = false)
    {
      serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.IP);
      serverSocket.Bind(new IPEndPoint(IPAddress.Any, 8080));
      serverSocket.Listen(256);

      System.Console.WriteLine("Press Ctrl + c to stop server");
      int counter = 0;
      for (int count = 0; count < 100000; count++)
      {
          sockIdList.Add(count);
      }
      ThreadPool.QueueUserWorkItem(new WaitCallback(publishAll));

      while (!Console.KeyAvailable)
      {
        Socket ws = serverSocket.Accept();
        ThreadPool.QueueUserWorkItem(new WaitCallback(OnAcceptConnection), ws);

        counter++;
      }

      shutDown = true;
    }

    private void checkSaveOrOpen(long uniqueId, ref string data)
    {
        string[] dataRcvd = data.Split(new char[] {','}, 3);
        if (string.Compare(dataRcvd[0], "s") == 0)
        {
            try
            {
                data = 'u' + data.Remove(0, 1);
                File.WriteAllText(dataRcvd[1], data);
            }
            catch (Exception ex)
            {
                System.Console.WriteLine("Error: Save failed for clientId: {0}, {1}", uniqueId, ex.Message);
            }
        }
        else if (string.Compare(dataRcvd[0], "Ready") == 0)
        {
            try
            {
                data = File.ReadAllText(dataRcvd[1]);
            }
            catch (Exception ex)
            {
                System.Console.WriteLine("Error: Open failed for clientId: {0}, {1}", uniqueId, ex.Message);
                data = initData;
            }
        }

    }

    public void setStreamData(long uniqueId, string data)
    {
        checkSaveOrOpen(uniqueId, ref data);
      lock (lockObj)
      {
        StringBuilder sb = null;
        if (currClientData.TryGetValue(uniqueId, out sb))
        {
            sb.Clear();
            sb.Append(data);
            updatesAvailable.Add(uniqueId);
        }
        else
        {
           System.Console.WriteLine("Error: Internal error in setStreamData for Client Id: {0}, disconnecting", uniqueId);
           cleanupUniqueId(uniqueId);
        }
      }

      eventDataAvailable.Set();
    }

    private void sendData_Completed(object sender, SocketAsyncEventArgs e)
    {
        long uniqueId = (long) e.UserToken;

        waitForClientResp(uniqueId);
        return;
    }

    private void publishAll(object dummy)
    {
      byte[] byteToSend = null;

      List<long> socksToRemove = new List<long>();
      while(!shutDown)
      {
        bool dataReceived = eventDataAvailable.WaitOne(1000);
        if (shutDown) break;
        if (dataReceived)
        {
          lock (lockObj)
          {
              HashSet<long>.Enumerator enumSockets = updatesAvailable.GetEnumerator();
              
            while (enumSockets.MoveNext())
            {
              try
              {
                  encodeData(getStreamData((int) enumSockets.Current), ref byteToSend);
                  SocketAsyncEventArgs sendData = new SocketAsyncEventArgs();
                  sendData.UserToken = (object)enumSockets.Current;
                  sendData.Completed += sendData_Completed;
                  sendData.SetBuffer(byteToSend, 0, byteToSend.Length);

                  Socket sockToUpdate = null;

                  if (!socketsToUpdate.TryGetValue(enumSockets.Current, out sockToUpdate))
                  {
                      System.Console.WriteLine("Internal error on Socket: {0}", enumSockets.Current);
                      Trace.TraceError("Internal error on Socket: {0}", enumSockets.Current);
                      socksToRemove.Add(enumSockets.Current);
                  }
                  else
                  {
                      if (sockToUpdate.Available == 0 && sockToUpdate.Poll(1000, SelectMode.SelectRead)) throw new Exception("Client Closed Connection");
                      sockToUpdate.SendAsync(sendData);
                  }

              }
              catch (Exception ex)
              {
                System.Console.WriteLine("Client Closed: {0}", enumSockets.Current);
                Trace.TraceError("Socket Error: {0}", ex.Message);
                socksToRemove.Add(enumSockets.Current);
              }
            }

            foreach (long socketId in socksToRemove)
            {
                cleanupUniqueId(socketId);
                //socketsToUpdate.Remove(socketId);
                System.Console.WriteLine("Client: {0} removed from list", socketId);
            }
            socksToRemove.Clear();
          }

        }

      }
    }

    
    private string processStreamData(int uniqueId)
    {
        StringBuilder retVal = new StringBuilder();

        StringBuilder streamData = null;
        if (!currClientData.TryGetValue(uniqueId, out streamData))
        {
            streamData.Append("Error");
        }
        
        string[] dataOrig = streamData.ToString().Split(',').Select(s => s.Trim()).ToArray();
        string[] data;

        int rows = 0, cols = 0;
        HashSet<int> computedCols = new HashSet<int>();

        if (dataOrig.Length >= 4)
        {
            rows = int.Parse(dataOrig[2]);
            cols = int.Parse(dataOrig[3]);
            retVal.AppendFormat("{0}, {1}, {2}, {3}", dataOrig[0], dataOrig[1], dataOrig[2], dataOrig[3]);
            data = Enumerable.Repeat("", rows * cols).ToArray();
            bool btitleFilled = false;

            int dataPos = 4;

            Dictionary<string, string> formulaCols = new Dictionary<string,string>();
            Dictionary<string, List<int>> formulaDepend = new Dictionary<string, List<int>>();
            for (int colCount = 0; colCount < cols && dataOrig.Length > colCount + dataPos; colCount++)
            {
                string title = dataOrig[dataPos + colCount];
                data[colCount] = title;

                if (dataOrig[dataPos + colCount].Contains("="))
                {
                    computedCols.Add(colCount);
                    title = dataOrig[dataPos + colCount].Substring(0, dataOrig[dataPos + colCount].IndexOf("="));
                    string formula = dataOrig[dataPos + colCount].Substring(dataOrig[dataPos + colCount].IndexOf("=") + 1, dataOrig[dataPos + colCount].Length - dataOrig[dataPos + colCount].IndexOf("=") - 1);
                    formulaCols.Add(dataOrig[dataPos + colCount], title);
                    fastPrsr.addNewFormula(title, formula);

                    List<int> colDepend = new List<int>();
                    for (int colCounter = 0; colCounter < cols; colCounter++)
                    {
                        if (dataOrig[dataPos + colCounter].Trim().Length > 0 && formula.Contains(dataOrig[dataPos + colCounter]) )
                        {
                            colDepend.Add(colCounter);
                        }
                    }
                    formulaDepend.Add(dataOrig[dataPos + colCount], colDepend);

                }
                retVal.AppendFormat(",{0}", dataOrig[dataPos + colCount]);
                btitleFilled = true;
            }

            for (int row = 1; row < rows; row++)
            {
                for (int col = 0; col < cols; col++)
                {
                    if (computedCols.Contains(col) || dataOrig.Length <= dataPos + row * cols + col)
                    {
                        data[row * cols + col] = "";
                    }
                    else
                    {
                        data[row * cols + col] = dataOrig[dataPos + row * cols + col];
                    }
                }
            }

            if (!btitleFilled)
            {
                for (int colCount = 0; colCount < cols; colCount++)
                {
                    retVal.AppendFormat("," );
                }
            }


            for (int rowCount = 1; rowCount < rows; rowCount++)
            {
                for (int colCount = 0; colCount < cols && data.Length >  rowCount * cols + colCount; colCount++)
                {
                    string dataToAdd = string.Empty;
                    if (data[colCount].Contains("="))
                    { 
                        string title;
                        if (formulaCols.TryGetValue(data[colCount], out title))
                        {
                            List<int> depends;
                            if (formulaDepend.TryGetValue(data[colCount], out depends))
                            {
                                bool allEmpty = true;
                                Dictionary<string, double> dependValues = new Dictionary<string, double>();
                                foreach (int depend in depends)
                                {
                                    double value;
                                    if (double.TryParse(data[rowCount * cols + depend], out value))
                                    {
                                        allEmpty = false;
                                        dependValues.Add(data[depend], value);
                                    }
                                    else
                                    {
                                        dependValues.Add(data[depend], 0.0);
                                    }
                                }

                                if (!allEmpty)
                                {
                                    dataToAdd = fastPrsr.computeFormula(title, dependValues).ToString();
                                }
                                else
                                {
                                    dataToAdd = string.Empty;
                                }

                            }
                        }
                    }
                    else
                    {
                        dataToAdd = data[rowCount * cols + colCount];
                    }
                    retVal.AppendFormat(",{0}", dataToAdd);

                }
            }
        }

        return retVal.ToString();
    }

    private string getStreamData(int uniqueId)
    {
       return processStreamData(uniqueId);
    }

    private string getStrFromByte(byte[] bytes, int sizeLen)
    {
      return Encoding.UTF8.GetString(bytes, 0, sizeLen);
    }

    private string getKeyFromHeader(string header)
    {
      string keyStr = header.Replace("ey:", "`")
                             .Split('`')[1]
                             .Replace("\r", "").Split('\n')[0]
                             .Trim();
      return keyStr;
    }

    private void prepareClientResponse(string header, ref byte[] retVal)
    {

      string key = getKeyFromHeader(header);
      string newKey = key + uniqueID;

      byte[] hashBytesToSend = sha1Crypto.ComputeHash(Encoding.ASCII.GetBytes(newKey));

      string respkey = Convert.ToBase64String(hashBytesToSend);

      string clientResp = "HTTP/1.1 101 Switching Protocols\r\n"
                         + "Upgrade: websocket\r\n"
                         + "Connection: Upgrade\r\n"
                         + "Sec-WebSocket-Accept: " + respkey + "\r\n\r\n";
      //+"Sec-WebSocket-Protocol: chat, superchat\r\n" 
      //+ "Sec-WebSocket-Version: 13\r\n";

      Trace.TraceInformation("prepareClientResponse -- Sending Response to Client: {0}", clientResp);

      retVal = Encoding.UTF8.GetBytes(clientResp);

    }

    private bool canDecode(byte[] data, int size)
    {
        bool bRetVal = false;

        if (size > 2)
        {
            byte dataType = data[0];
            byte[] mask = new byte[4];

            Trace.TraceInformation("Data Type = {0}", dataType);

            byte dataLength = (byte)(data[1] & (byte)0x80);

            int lengthDataByte = 0;

            if (dataLength == 126)
            {
                lengthDataByte = 2;
            }
            else if (dataLength == 127)
            {
                lengthDataByte = 8;
            }
            else
            {
                lengthDataByte = data[1] & 0x7f;
            }

            if (size > 2 + lengthDataByte)
            {
                int finalLength = 0;

                if (lengthDataByte == 2 || lengthDataByte == 8)
                {
                    for (int counter = 0; counter < lengthDataByte; counter++)
                    {
                        finalLength <<= data[2 + counter];
                    }
                }
                else
                {
                    finalLength = lengthDataByte;
                    lengthDataByte = 0;
                }

                if (size >= finalLength + 2 + lengthDataByte + 4)
                {
                    bRetVal = true;
                    //mask[0] = data[2 + lengthDataByte];
                    //mask[1] = data[2 + lengthDataByte + 1];
                    //mask[2] = data[2 + lengthDataByte + 2];
                    //mask[3] = data[2 + lengthDataByte + 3];

                    //byte[] buffer = new byte[finalLength];

                    //for (int counter = 0; counter < finalLength; counter++)
                    //{
                    //    buffer[counter] = (byte)((data[2 + lengthDataByte + 4 + counter]) ^ (mask[counter % 4]));
                    //}
                }
            }

            //return Encoding.UTF8.GetString(buffer);
        }

        return bRetVal;

    }

    private bool decodeData(byte[] data, int length, ref string decoded)
    {
        decoded = string.Empty;

        byte dataType = data[0];
        byte[] mask;

        Trace.TraceInformation("Data Type = {0}", dataType);

        byte dataLength = data[1];
        int keyIndex = 0;
        int finalLength = 0;

        int lengthDataByte = 0;

        if (dataLength - 128 <= 125)
        {
            lengthDataByte = dataLength - 128;
            keyIndex = 2;
            finalLength = lengthDataByte + 6;
        }
        else if (dataLength - 128 == 126)
        {
            if (length < 4) return false;
            lengthDataByte = BitConverter.ToInt16(new byte[] { (byte)data[3], (byte)data[2] }, 0);
            keyIndex = 4;
            finalLength = lengthDataByte + 8;
        }
        else
        {
            if (length < 10) return false;
            lengthDataByte = (int)BitConverter.ToInt64(new byte[] { data[9], data[8], data[7], data[6], data[5], data[4], data[3], data[2] }, 0);
            keyIndex = 10;
            finalLength = lengthDataByte + 14;
        }

        if (finalLength > length) return false;

        mask = new byte[] { data[keyIndex], data[keyIndex + 1], data[keyIndex + 2], data[keyIndex + 3] };

        byte[] buffer = new byte[finalLength - ( keyIndex + 4)];

        for (int counter = 0; counter + keyIndex + 4< finalLength; counter++)
        {
            buffer[counter] = (byte)((data[counter + keyIndex + 4]) ^ (mask[counter % 4]));
        }
        decoded = Encoding.UTF8.GetString(buffer);
        
        return true;

    }

    //private string decodeData(byte[] data)
    //{
    //  byte dataType = data[0];
    //  byte[] mask = new byte[4];

    //  Trace.TraceInformation("Data Type = {0}", dataType);

    //  byte dataLength = (byte)(data[1] & (byte)0x80);

    //  int lengthDataByte = 0;

    //  if (dataLength == 126)
    //  {
    //    lengthDataByte = 2;
    //  }
    //  else if (dataLength == 127)
    //  {
    //    lengthDataByte = 8;
    //  }
    //  else
    //  {
    //    lengthDataByte = data[1] & 0x7f;
    //  }

    //  int finalLength = 0;

    //  if (lengthDataByte == 2 || lengthDataByte == 8)
    //  {
    //    for (int counter = 0; counter < lengthDataByte; counter++)
    //    {
    //      finalLength <<= data[2 + counter];
    //    }
    //  }
    //  else
    //  {
    //    finalLength = lengthDataByte;
    //    lengthDataByte = 0;
    //  }

    //  mask[0] = data[2 + lengthDataByte];
    //  mask[1] = data[2 + lengthDataByte + 1];
    //  mask[2] = data[2 + lengthDataByte + 2];
    //  mask[3] = data[2 + lengthDataByte + 3];

    //  byte[] buffer = new byte[finalLength];

    //  for (int counter = 0; counter < finalLength; counter++)
    //  {
    //    buffer[counter] = (byte)((data[2 + lengthDataByte + 4 + counter]) ^ (mask[counter % 4]));
    //  }

    //  return Encoding.UTF8.GetString(buffer);

    //}

    private void encodeData(string data, ref byte[] dataByte)
    {
      int lengthOfData = data.Length;
      int rawByteStart = 2;

      if (lengthOfData <= 125)
      {
        dataByte = new byte[lengthOfData + 2];
        dataByte[0] = 129;
        dataByte[1] = (byte)lengthOfData;

      }
      else if (lengthOfData > 125 && lengthOfData <= 65535)
      {
        dataByte = new byte[lengthOfData + 4];
        dataByte[0] = 129;
        dataByte[1] = 126;
        dataByte[2] = (byte)((lengthOfData >> 8) & 0xff);
        dataByte[3] = (byte)((lengthOfData) & 0xff);
        rawByteStart = 4;
      }
      else
      {
        dataByte = new byte[lengthOfData + 10];
        dataByte[0] = 129;

        dataByte[2] = (byte)((lengthOfData >> 56) & 255);
        dataByte[3] = (byte)((lengthOfData >> 48) & 255);
        dataByte[4] = (byte)((lengthOfData >> 40) & 255);
        dataByte[5] = (byte)((lengthOfData >> 32) & 255);
        dataByte[6] = (byte)((lengthOfData >> 24) & 255);
        dataByte[7] = (byte)((lengthOfData >> 16) & 255);
        dataByte[8] = (byte)((lengthOfData >> 8) & 255);
        dataByte[9] = (byte)((lengthOfData) & 255);
        rawByteStart = 10;
      }

      Encoding.UTF8.GetBytes(data, 0, data.Length, dataByte, rawByteStart);
    }

    private string getRandomNumbers()
    {
      var r = new Random();
      StringBuilder sbRet = new StringBuilder();
      int width = 800;
      int height = 350;
      int rowNum = 10;
      int colNum = 10;

      sbRet.AppendFormat("{0},{1},{2},{3}", width, height, rowNum + 1, colNum);

      for (int col = 0; col < colNum; col++)
      {
        sbRet.Append(",");
        sbRet.AppendFormat("Colmn{0}", col);

      }

      for (int row = 0; row < rowNum; row++)
      {
        for (int col = 0; col < colNum; col++)
        {
          sbRet.Append(",");
          sbRet.AppendFormat("{0}", (float)r.Next(100000) / (float)100.0);

        }
      }

      return sbRet.ToString();
    }


    private void OnAcceptConnection(object param)
    {
      byte[] buffer = new byte[1024];
      byte[] sendBuffer = null;
      Socket client = (Socket)param;
      string headerResponse = string.Empty;
      long uniqueId = -1;
      string clientResp = string.Empty;

      try
      {
        if (serverSocket != null && serverSocket.IsBound)
        {
          int rcvBytes = client.Receive(buffer);
          headerResponse = getStrFromByte(buffer, rcvBytes);

          if (client != null)
          {
            prepareClientResponse(headerResponse, ref sendBuffer);
            client.Send(sendBuffer);

            rcvBytes = client.Receive(buffer);
            string dataFromClient = string.Empty;
            decodeData(buffer, rcvBytes, ref dataFromClient);
            Trace.TraceInformation("Data from Client: {0}", dataFromClient);

            Trace.TraceInformation("OnAcceptConnection -- Received {0} bytes: {1}", rcvBytes, dataFromClient);
            string[] clientInitialData = dataFromClient.Split(new char[] { ',' }, 3);
            if (string.Compare(clientInitialData[0], "Ready") == 0)
            {
                try
                {
                    clientResp = File.ReadAllText(clientInitialData[1]);
                    if (clientResp.Length <= 0)
                    {
                        clientResp = initData;
                    }
                }
                catch (Exception ex)
                {
                    System.Console.WriteLine("File Read error for Client: {0}", uniqueId);
                    Trace.TraceError("Error: File Read error for Client: {0}, {1}", uniqueId, ex.Message);
                    clientResp = initData;
                }
            }
            else
            {
                // Unknown client
                return;
            }
            uniqueId = getUniqueId();
              
            socketState sockState = new socketState();
            StringBuilder currCltData = new StringBuilder();
            sockState.sockId = uniqueId;
            sockState.sockClient = client;
            sockState.bytesReceived = 0;
            byte[] clientData = new byte[maxBufferSize];
     
            //beginRcv(client, clientData, 0, (object)sockState);
            //client.BeginReceive(clientData, 0, (int) maxBufferSize, SocketFlags.None, new AsyncCallback(onClientBufferReceive), (object)sockState);
                // If Active Client connection management is not required the below lines can be commented.  
                //while (true) 
                //{
                //  if (shutDown) return;
                //  Thread.Sleep(1000);
                //    if (client.Available == 0 && client.Poll(1000, SelectMode.SelectRead)) throw new Exception("Client Closed Connection");
                //}
            lock (lockObj)
            {
                clientList.Add(uniqueId, clientData);
                socketsToUpdate.Add(uniqueId, client);
                currClientData.Add(uniqueId, currCltData);
            }

            System.Console.WriteLine("Serving Client # {0}", uniqueId);
            setStreamData(uniqueId, clientResp);

          }
        }
        else
        {
          Trace.TraceError("Error: ServerSocket is NULL or IsBount = false");
        }
      }
      catch (Exception ex)
      {
        Trace.TraceError("Error: Exception: {0}", ex.Message);
        System.Console.WriteLine("Error in Socket: {0} -- removing", uniqueId);
        cleanupUniqueId(uniqueId);
      }
      //finally
      //{
      //  unSubscribe(client);
      //  System.Console.WriteLine("Client Closed # {0}", System.Threading.Thread.CurrentThread.ManagedThreadId);
      //  System.Console.WriteLine("Connection Closed");
      //  client.Close();
      //}
 
    }

    private void waitForClientResp(long uniqueId)
    {
        byte[] dataBuff = null;
        Socket client = null;

        lock (lockObj)
        {
            if (clientList.TryGetValue(uniqueId, out dataBuff))
            {
                if (socketsToUpdate.TryGetValue(uniqueId, out client))
                {
                    socketState sockState = new socketState();
                    sockState.sockId = uniqueId;
                    sockState.sockClient = client;
                    sockState.bytesReceived = 0;
                    beginRcv(client, dataBuff, 0, sockState);
                    System.Console.WriteLine("Awaiting Client Response: {0}", uniqueId);

                }
                else
                {
                    Trace.TraceError("Error: Internal error, cannot find client Socket: {0}", uniqueId);
                    System.Console.WriteLine("Error: Internal error, cannot find client Socket: {0}", uniqueId);
                    cleanupUniqueId(uniqueId);
                }
            }
            else
            {
                Trace.TraceError("Error: Internal error, cannot find clientId: {0}", uniqueId);
                System.Console.WriteLine("Error: Internal error, cannot find clientId: {0}", uniqueId);

                cleanupUniqueId(uniqueId);
            }
        }
    }

    private void beginRcv(Socket client, byte[] buff, long offset, object sockState)
    {
        client.BeginReceive(buff, (int) offset, (int)maxBufferSize, SocketFlags.None, new AsyncCallback(onClientBufferReceive), (object)sockState);

    }

    private void onClientBufferReceive(IAsyncResult result)
    {
        long uniqueId = -1;
        try
        {
            socketState sockState = (socketState)result.AsyncState;
            uniqueId = sockState.sockId;
            System.Console.WriteLine("responding to Client: {0}", uniqueId);
            Trace.TraceInformation("responding to Client: {0}", uniqueId);
            if (sockState != null)
            {
                int bytesReceived = sockState.sockClient.EndReceive(result);
                if (bytesReceived > 0)
                {
                    lock (lockObj)
                    {
                        byte[] dataRcvd;
                        if (clientList.TryGetValue(uniqueId, out dataRcvd))
                        {
                            StringBuilder sbData;
                            if (currClientData.TryGetValue(uniqueId, out sbData))
                            {
                                sockState.bytesReceived += bytesReceived;
                                string decodedData = string.Empty;
                                if (decodeData(dataRcvd, (int)sockState.bytesReceived, ref decodedData))
                                {
                                    sbData.Clear();
                                    sbData.Append(decodedData);
                                    // Process client data

                                    if (string.Compare(decodedData, "##CLOSE##") == 0) throw new Exception("Client closed, purge: " + uniqueId.ToString());
                                    setStreamData(uniqueId, sbData.ToString());
                                    sockState.bytesReceived = 0;
                                    System.Console.WriteLine("Success responding to Client: {0}", uniqueId);
                                    Trace.TraceInformation("Success responding to Client: {0}", uniqueId);
                                }
                                else
                                {
                                    Trace.TraceInformation("Awaiting more bytes from Client: {0}, bytes rcvd: {1}", uniqueId, sockState.bytesReceived);
                                    System.Console.WriteLine("Awaiting more bytes from Client: {0}, bytes rcvd: {1}", uniqueId, sockState.bytesReceived);
                                    beginRcv(sockState.sockClient, dataRcvd, sockState.bytesReceived, (object) sockState);
                                }
                            }
                            else
                            {
                                Trace.TraceError("Internal Error: Client Id - {0}", uniqueId);
                                System.Console.WriteLine("Internal Error: Client Id - {0}, not found", uniqueId);
                            }

                        }
                        else
                        {
                            Trace.TraceError("Internal Error: Client Id {0}", uniqueId);
                            System.Console.WriteLine("Internal Error: Client Id {0}, not found", uniqueId);
                        }
                    }
                }
                else
                {
                    Trace.TraceError("0 bytes received from Client: {0}", uniqueId);
                    System.Console.WriteLine("0 bytes received from Client: {0}", uniqueId);
                }

            }
            else
            {
                Trace.TraceError("Unknown Client Connection, cannot process");
                System.Console.WriteLine("Unknown Client Connection, cannot process");
            }
        }
        catch (Exception ex)
        {
            Trace.TraceError("Client Connection Close: {0}, {1}", uniqueId, ex.Message);
            System.Console.WriteLine("Client Connection Close: {0}", uniqueId);
            cleanupUniqueId(uniqueId);
        }

    }
  }
}
