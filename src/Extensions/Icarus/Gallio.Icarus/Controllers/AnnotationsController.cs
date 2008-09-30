﻿using System.Collections.Generic;
using System.ComponentModel;
using Gallio.Icarus.Controllers.Interfaces;
using Gallio.Model;
using Gallio.Model.Serialization;

namespace Gallio.Icarus.Controllers
{
    class AnnotationsController : IAnnotationsController
    {
        private readonly ITestController testController;
        private readonly List<AnnotationData> annotationsList = new List<AnnotationData>();
        private readonly BindingList<AnnotationData> annotations;
        private bool showErrors = true, showWarnings = true, showInfo = true;

        public BindingList<AnnotationData> Annotations
        {
            get { return annotations; }
        }

        public bool ShowErrors
        {
            get { return showErrors; }
            set
            {
                showErrors = value;
                UpdateList();
            }
        }

        public bool ShowWarnings
        {
            get { return showWarnings; }
            set
            {
                showWarnings = value;
                UpdateList();
            }
        }

        public bool ShowInfo
        {
            get { return showInfo; }
            set
            {
                showInfo = value;
                UpdateList();
            }
        }

        public string ErrorsText { get; private set; }

        public string WarningsText { get; private set; }

        public string InfoText { get; private set; }

        public AnnotationsController(ITestController testController)
        {
            this.testController = testController;
            annotations = new BindingList<AnnotationData>(annotationsList);
            testController.LoadFinished += delegate { UpdateList(); };
        }

        private void UpdateList()
        {
            annotations.Clear();
            int error = 0, warning = 0, info = 0;
            testController.Report.Read(report =>
            {
                foreach (AnnotationData annotationData in report.TestModel.Annotations)
                {
                    switch (annotationData.Type)
                    {
                        case AnnotationType.Error:
                            if (showErrors)
                                annotations.Add(annotationData);
                            error++;
                            break;
                        case AnnotationType.Warning:
                            if (showWarnings)
                                annotations.Add(annotationData);
                            warning++;
                            break;
                        case AnnotationType.Info:
                            if (showInfo)
                                annotations.Add(annotationData);
                            info++;
                            break;
                    }
                }
                ErrorsText = string.Format("{0} Errors", error);
                WarningsText = string.Format("{0} Warnings", warning);
                InfoText = string.Format("{0} Info", info);
            });
        }
    }
}