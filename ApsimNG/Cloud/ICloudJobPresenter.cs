﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UserInterface.Interfaces
{
    /// <summary>
    /// Interface defining standard functionality which all presenters managing a cloud job display view much implement.
    /// </summary>
    public interface ICloudJobPresenter
    {
        /// <summary>
        /// Parses and compares two DateTime objects stored as strings.
        /// </summary>
        /// <param name="str1">First DateTime.</param>
        /// <param name="str2">Second DateTime.</param>
        /// <returns></returns>
        int CompareDateTimeStrings(string str1, string str2);

        /// <summary>
        /// Gets the formatted display name of a job.
        /// </summary>
        /// <param name="id">ID of the job.</param>
        /// <param name="withOwner">If true, the return value will include the job owner's name in parentheses.</param>
        /// <returns></returns>
        string GetJobName(string id, bool withOwner);

        /// <summary>
        /// Checks if the current user owns a job. 
        /// </summary>
        /// <param name="id">ID of the job.</param>
        /// <returns></returns>
        bool UserOwnsJob(string id);

        /// <summary>
        /// Asks the user for confirmation and then halts execution of a list of jobs.
        /// </summary>
        /// <param name="id">ID of the jobs to be stopped.</param>
        void StopJobs(List<string> jobIds);

        /// <summary>
        /// Asks the user for confirmation and then deletes a list of jobs.
        /// </summary>
        /// <param name="jobIds">IDs of the jobs to be deleted.</param>
        void DeleteJobs(List<string> jobIds);

        /// <summary>
        /// Downloads the results of all jobs with given IDs.
        /// </summary>
        /// <param name="jobIds">List of job IDs.</param>
        /// <param name="saveToCsv">If true, results will be combined into a single CSV file.</param>
        /// <param name="includeDebugFiles">If true, debugging files will be saved.</param>
        /// <param name="keepOutputFiles">If true, raw (.db) output files will be saved.</param>
        void DownloadResults(List<string> jobIds, bool saveToCsv, bool includeDebugFiles, bool keepOutputFiles);

        /// <summary>
        /// Opens a dialog box which asks the user for credentials.
        /// </summary>
        void SetupCredentials();

        /// <summary>
        /// Sets the default downlaod directory.
        /// </summary>
        /// <param name="dir">Path to the directory.</param>
        void SetDownloadDirectory(string dir);
    }
}
