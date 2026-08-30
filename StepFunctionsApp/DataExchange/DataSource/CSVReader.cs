using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Diagnostics;
using System.Text;

namespace StepFlow.DataModel.Entities.DataSource
{
    // Reads the CSV file into an collection of type ImportSchema->CustomerSchemaObject
    public class CSVReader
    {
        [Key]
        public int Id { get; set; }

        public long RecordCount = 0;

        public List<string[]> ReadDynamicCSVFile(string path, int startAtRow = 0, int skipFooter = 0, char delimiter = ',')
        {
            List<string[]> Entities = new List<string[]>();
            Console.WriteLine("Enter ...");

            string directory = string.Empty;
            string file = string.Empty;

            try
            {
                long recordCount = 0;
                int skipTotal = startAtRow;

                FileStream csvFileStream = null; ;
                StreamReader sr = null;

                directory = Path.GetDirectoryName(path);
                file = Path.GetFileName(path);

                if (CheckMaxFileSize(path, 1000000))
                {
                    Console.WriteLine("File is to large to process: " + path);
                    return null;
                }

                csvFileStream = File.OpenRead(path);
                sr = new StreamReader(csvFileStream);

                string nextLine = "";

                if (startAtRow != 0)
                {
                    // Norm the position in file to zero based count.
                    startAtRow--;
                }

                while (nextLine != null)
                {
                    recordCount++;

                    // Skip the header - Dont process
                    if (startAtRow != 0)
                    {
                        startAtRow--;
                        nextLine = sr.ReadLine();
                        continue;
                    }

                    // Skip the footer - Dont process
                    if ((recordCount - skipTotal) + skipFooter >= RecordCount)
                    {
                        nextLine = sr.ReadLine();
                        continue;
                    }

                    string[] parameters = nextLine.Split(delimiter);

                    Entities.Add(parameters);

                    nextLine = sr.ReadLine();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error with file:" + path + " ,Reason:" + ex.Message);
                return null;
            }
            Console.WriteLine("Exit ...");
            return Entities;
        }

        public DataTable CreateXSDSchemaFromHeader(string path, string schemaName, int startAtRow, char delimiter = ',')
        {

            Console.WriteLine("Enter ...");

            string directory = string.Empty;
            string file = string.Empty;
            try
            {
                FileStream csvFileStream = null; ;
                StreamReader sr = null;

                directory = Path.GetDirectoryName(path);
                file = Path.GetFileName(path);

                if (CheckMaxFileSize(path, 1000000))
                {
                    Console.WriteLine("File is to large to process: " + path);
                    return null;
                }

                csvFileStream = File.OpenRead(path);
                sr = new StreamReader(csvFileStream);

                string nextLine = "";

                if (startAtRow != 0)
                {
                    // Norm the position in file to zero based count.
                    startAtRow--;
                    sr.ReadLine();
                }

                while (nextLine != null)
                {
                    // Skip the header - Dont process
                    if (startAtRow != 0)
                    {
                        startAtRow--;
                        nextLine = sr.ReadLine();
                        continue;
                    }

                    string[] columns = nextLine.Split(delimiter);

                    DataTable customerSchema = new DataTable(schemaName.Replace(" ", "_"));
                    customerSchema.Namespace = schemaName.Replace(" ", "_");
                    customerSchema.AcceptChanges();

                    foreach (var col in columns)
                    {
                        // Set up the columns in the Tables
                        DataColumn column = new DataColumn(col.Trim().Replace('"', ' ').Trim(), typeof(String));
                        customerSchema.Columns.Add(column);
                    }

                    customerSchema.AcceptChanges();

                    return customerSchema;

                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error with file:" + path + " ,Reason:" + ex.Message);
            }
            Console.WriteLine("Exit ...");
            return null;
        }



        /// <summary>
        /// This will convert a CSV to an in memory XML dataset from an given xsd.
        /// </summary>
        /// <param name="strDataFileName"></param>
        /// <returns>DataSet</returns>
        public (DataSet ds, StringBuilder errors, StringBuilder messages) ReadCSVFile(Stream schemaXML, string csvFile, int startAtRow = 0, int skipFooter = 0, char delimiter = ',', bool skipBad = true)
        {
            // Output.
            StringBuilder errors = new StringBuilder();
            StringBuilder messages = new StringBuilder();
            DataSet dsOut = new DataSet();

            Stopwatch stopWatch = new Stopwatch();
            stopWatch.Start();

            messages.AppendLine("ReadCSVFile Started...");

            long recordCount = 0;
            int skipTotal = startAtRow;

            string directory = string.Empty;
            string file = string.Empty;
            string newFile = string.Empty;

            try
            {

                FileStream csvFileStream = null; ;
                StreamReader sr = null;

                directory = Path.GetDirectoryName(csvFile);
                file = Path.GetFileName(csvFile);

                dsOut.ReadXmlSchema(schemaXML);
                dsOut.AcceptChanges();

                if (CheckMaxFileSize(csvFile, 1000000))
                {
                    errors.Append("File is to large to process: " + csvFile);
                    return (null, errors, messages);
                }

                csvFileStream = File.OpenRead(csvFile);
                sr = new StreamReader(csvFileStream);

                dsOut.Tables[0].BeginLoadData();

                string str;
                string nextLine = "";

                if (startAtRow != 0)
                {
                    // Norm the position in file to zero based count.
                    startAtRow--;
                    sr.ReadLine();
                }

                while (nextLine != null)
                {
                    recordCount++;

                    // Skip the header - Dont process
                    if (startAtRow != 0 && startAtRow != -1)
                    {
                        startAtRow--;
                        nextLine = sr.ReadLine();
                        continue;
                    }

                    // Skip the footer - Dont process
                    if ((recordCount - skipTotal) + skipFooter >= RecordCount)
                    {
                        nextLine = sr.ReadLine();
                        continue;
                    }

                    if (startAtRow == 0)
                    {
                        startAtRow--;
                        nextLine = sr.ReadLine();
                    }

                    string[] parameters = nextLine.Split(delimiter);


                    if (dsOut.Tables[0].Columns.Count != parameters.Length)
                    {
                        messages.AppendLine("Row: " + recordCount + "\r\n Data:" + nextLine + "\r\n");

                        errors.AppendLine("Row: " + recordCount + "\r\nThe CSV file column count is incorrect. File: " + file + ", XML Schema:" + dsOut.Tables[0].Columns.Count + " != CSV:" + parameters.Length);

                        if (!skipBad)
                        {
                            errors.AppendLine("Skip bad files enabled - Exit Reading operation ...");
                            return (null, errors, messages);
                        }

                        nextLine = sr.ReadLine();
                        continue;
                    }

                    DataRow datarow = dsOut.Tables[0].NewRow();

                    for (int k = 0; k < dsOut.Tables[0].Columns.Count; k++)
                    {
                        string colName = dsOut.Tables[0].Columns[k].ColumnName;
                        str = parameters[k];
                        if (str == null)
                        {
                            str = string.Empty;
                        }

                        datarow[k] = str;
                        str = string.Empty;
                    }
                    dsOut.Tables[0].Rows.Add(datarow);
                    nextLine = sr.ReadLine();
                }

                dsOut.AcceptChanges();

                dsOut.WriteXml("Imported_CSV_" + DateTime.Now.Ticks.ToString() + ".xml");
            }
            catch (Exception ex)
            {
                errors.AppendLine("Error with file:" + csvFile + " ,Reason:" + ex.Message);
                messages.AppendLine("ReadCSVFile Ended...");
                dsOut.RejectChanges();
                return (null, errors, messages);
            }
            stopWatch.Stop();
            // Get the elapsed time as a TimeSpan value.
            TimeSpan ts = stopWatch.Elapsed;

            // Format and display the TimeSpan value.
            string elapsedTime = String.Format("{0:00}:{1:00}:{2:00}.{3:00}",
                ts.Hours, ts.Minutes, ts.Seconds,
                ts.Milliseconds / 10);

            messages.AppendLine("ReadCSVFile Ended..." + "RunTime: " + elapsedTime);
            return (dsOut, errors, messages);
        }

        public bool CheckMaxFileSize(string strFileName, int nMaxRecordSize)
        {
            bool isMax = false;
            try
            {
                long count = 0;
                using (StreamReader r = new StreamReader(strFileName))
                {
                    string line;
                    while ((line = r.ReadLine()) != null)
                    {
                        count++;
                    }
                }
                if (count > nMaxRecordSize)
                {
                    isMax = true;
                }

                RecordCount = count;

                return isMax;
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
            return false;
        }

    }
}
