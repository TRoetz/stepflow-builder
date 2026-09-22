using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
/*

Core Principles

Dynamic UI Generation: The system enables the creation of diverse user interfaces by defining UI elements as C# entities and rendering them dynamically using jQuery.
Data-Driven: UI elements are linked to an underlying data model (AttributeDomain) for seamless data binding and persistence.
API-Centric: Communication between the frontend and backend occurs through an API, facilitating data exchange and UI updates.
Hierarchical Structure: UI elements can be nested to create complex layouts, culminating in a Page entity that represents the complete user interface.
Key Components

UI Elements:

TextBox, Dropdown, Checkbox, RadioButton, TextArea, FileUpload, DatePicker, Slider, StaticText, Button, InputGroup.
Each element has properties to control its appearance, behavior, and data binding.
Layout Elements:

Grid, GridItem, Section.
These elements provide structure and organization to the UI, allowing for the creation of grids and sections.

Page:
Represents the complete page structure, containing a hierarchy of UI elements.
Properties like Title and RootElements define the page's content.

AttributeDomain:
Serves as the data model for storing information captured by the UI elements.
UI elements are mapped to AttributeDomain properties for persistence.

Rendering Process:

API Request: The frontend requests the Page object from the API.
Element Rendering: jQuery functions (or Generic JS) are used to render each UIElement based on its type and properties.
Layout Construction: Layout elements like Grid and Section are used to arrange the UI elements on the page.
Data Binding: Data attributes or other mapping mechanisms connect UI elements to the AttributeDomain model.
Event Handling: Event handlers capture user interactions and update the data model accordingly.
API Submission: Modified data is sent back to the API for persistence.

Benefits:

Flexibility: Easily create various UI types and layouts without modifying frontend code.
Maintainability: Centralized management of UI structure and data binding in C# entities.
Efficiency: Dynamic rendering reduces page load times and improves user experience.
Scalability: The hierarchical structure allows for complex UIs and interactions.

Core Entities:

UIElement:

Id (int) - Unique identifier for the UI element.
Type (UIElementType enum) - Type of the UI element (e.g., Wizard, Step, TextBox, Dropdown).
Label (string) - Display label for the element.
Description (string) - Optional description of the element.
AttributeDomainId (int) - Maps the UI element to an AttributeDomain for data storage.
Parent (UIElement) - Reference to the parent UI element (e.g., a Step's parent would be a Wizard).
Children (List&lt;UIElement>) - Collection of child UI elements (e.g., Steps within a Wizard).
Wizard

(Inherits from UIElement)
Steps (List&lt;Step>) - Ordered collection of Steps within the Wizard.
Step

(Inherits from UIElement)
StepNumber (int) - Order of the step in the Wizard.
UIElements (List&lt;UIElement>) - Collection of UI elements within the Step.
TextBox

(Inherits from UIElement)
Placeholder (string) - Placeholder text for the TextBox.
MaxLength (int) - Maximum allowed length of input.
ValidationRegex (string) - Regular expression for input validation.
Dropdown

(Inherits from UIElement)
Options (List&lt;DropdownOption>) - Available options in the dropdown.
DropdownOption

Value (string) - The value of the option.
DisplayText (string) - Display text for the option.

EXAMPLES:

// Define a Wizard
Wizard myWizard = new Wizard()
{
    Id = 1,
    Label = "My Data Capture Wizard",
    Steps = new List<Step>()
    {
        new Step()
        {
            Id = 2,
            StepNumber = 1,
            Label = "Step 1: Basic Information",
            UIElements = new List<UIElement>()
            {
                new TextBox() { Id = 3, Label = "First Name",AttributeDomainId = 1, EntityAttributeId = 101 },
                new TextBox() { Id = 4, Label = "Last Name", AttributeDomainId = 1, EntityAttributeId = 102 }
            }
        },
        new Step()
        {
            Id = 5,
            StepNumber = 2,
            Label = "Step 2: Contact Details",
            UIElements = new List<UIElement>()
            {
                new TextBox() { Id = 6, Label = "Email", EntityAttributeId = 103 },
                new Dropdown()
                {
                    Id = 7,
                    Label = "Country",
                    AttributeDomainId = 104,
                    Options = new List<DropdownOption>()
                    {
                        new DropdownOption() { Value = "US", DisplayText = "United States" },
                        new DropdownOption() { Value = "CA", DisplayText = "Canada" }
                    }
                }
            }
        }
    }
};
*/

/*
// Define a page with a grid layout
Page myPage = new Page()
{
    // ...
    RootElements = new List<UIElement>()
    {
        new Grid()
        {
            Id = 201,
            Columns = 2,
            Rows = 2,
            GridItems = new List<GridItem>()
            {
                new GridItem()
                {
                    Id = 202,
                    Column = 1,
                    Row = 1,
                    UIElement = new TextBox() { Id = 203, Label = "Name" }
                },
                new GridItem()
                {
                    Id = 204,
                    Column = 2,
                    Row = 1,
                    UIElement = new Dropdown()
                    {
                        Id = 205,
                        Label = "Category",
                        Options = new List<DropdownOption>() {  }
                    }
                },
                new GridItem()
                {
                    Id = 206,
                    Column = 1,
                    Row = 2,
                    ColumnSpan = 2,
                    UIElement = new TextArea() { Id = 207, Label = "Description" }
                }
            }
        }
    }
};
*/

namespace StepFlow.DataModel.Entities.UiData
{
    public class UIPage
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string Author { get; set; }
        public int PageName { get; set; }
        public string PageVersion { get; set; }
        public virtual List<UIElement> RootElements { get; set; } = new(); // Navigation property
    }

    // Enum for UI element types
    public enum UIElementType
    {
        Wizard,
        Step,
        TextBox,
        Dropdown,
        DropdownOption,
        FileUpload,
        InputGroup,
        Checkbox,
        RadioButton,
        TextArea,
        DatePicker,
        Button,
        Slider,
        GridItem,
        Grid,
        Section,
        StaticText,
        Map
    }

    // Base class for all UI elements
    public class UIElement
    {
        public int Id { get; set; }
        
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public UIElementType Type { get; set; }
        public string Label { get; set; }
        public string Description { get; set; }
        public int AttributeDomainId { get; set; }
        public int EntityAttributeId { get; set; }
        
        public int? UIPageId { get; set; }
        public virtual UIPage UIPage { get; set; } // Navigation property
        
        public int? ParentId { get; set; }
        public virtual UIElement? Parent { get; set; } // Navigation property

        public virtual List<UIElement>? Children { get; set; } = new(); // Navigation property
    }
    
    public class MapLayer
    {
        public int Id { get; set; } // Or use another suitable primary key property
        public string Type { get; set; } // e.g., "GeoJson", "Symbol", "Heatmap"
        public string DataSource { get; set; } // URL or inline GeoJSON data
        public string StyleOptions { get; set; } // JSON string for layer styling options
        
        // Foreign Key to Map
        public int? MapId { get; set; } // Nullable to allow unassociated layers if necessary
        public virtual Map Map { get; set; } // Navigation property
        
    }
    
    public class Map : UIElement
    {
        /*
         Map:   This class defines the initial state and configuration of the Azure map.
                InitialCenter: Sets the starting coordinates of the map.
                InitialZoom: Determines the initial zoom level.
                MapStyle: Specifies the visual style of the map (road, satellite, etc.).
                Layers: Allows you to add multiple layers to the map, such as GeoJSON polygons, symbols, heatmaps, etc.
                MapLayer: Defines the type, data source, and style options for each layer.
                Rendering: The renderMap function initializes the Azure Maps control and adds layers dynamically based on the Layers property.
         */
        public string InitialCenter { get; set; } // e.g., "47.6062, -122.3321" (latitude, longitude)
        public int InitialZoom { get; set; } // Zoom level (1-20)
        public string MapStyle { get; set; } // e.g., "road", "satellite", "night"
        public virtual List<MapLayer> Layers { get; set; } = new(); // Define layers to display (see below)

        public Map()
        {
            Type = UIElementType.Map;
        }
    }

    // GridItem element
    public class GridItem : UIElement
    {
        public int Column { get; set; }
        public int Row { get; set; }
        public int ColumnSpan { get; set; }
        public int RowSpan { get; set; }

        public GridItem()
        {
            Type = UIElementType.GridItem;
        }
    }
    
    // Grid element
    public class Grid : UIElement
    {
        public int Columns { get; set; }
        public int Rows { get; set; }
        public int CellSpacing { get; set; }
        public int CellPadding { get; set; }

        public Grid()
        {
            Type = UIElementType.Grid;
        }
    }

    // Section element
    public class Section : UIElement
    {
        public string Heading { get; set; }

        public Section()
        {
            Type = UIElementType.Section;
        }
    }

    // Wizard element
    public class Wizard : UIElement
    {
        public Wizard()
        {
            Type = UIElementType.Wizard;
        }
    }

    // Step element
    public class Step : UIElement
    {
        public int StepNumber { get; set; }

        public Step()
        {
            Type = UIElementType.Step;
        }
    }

    // TextBox element
    public class TextBox : UIElement
    {
        public string Placeholder { get; set; }
        public int MaxLength { get; set; }
        public string? ValidationRegex { get; set; }

        public TextBox()
        {
            Type = UIElementType.TextBox;
        }
    }

    // Dropdown element
    public class Dropdown : UIElement
    {
        public Dropdown()
        {
            Type = UIElementType.Dropdown;
        }
    }

    // Dropdown option
    public class DropdownOption : UIElement
    {
        public string Value { get; set; }
        public string DisplayText { get; set; }
    }

    // Enum for input element types within an InputGroup
    public enum InputElementType
    {
        Text,
        Email,
        Number,
        Url,
        // ... other types as needed
    }
    
    // InputGroup element
    public class InputGroup : UIElement
    {
        public InputElementType InputElementType { get; set; }

        public InputGroup()
        {
            Type = UIElementType.InputGroup;
        }
    }

    // Checkbox element
    public class Checkbox : UIElement
    {
        public bool Checked { get; set; }

        public Checkbox()
        {
            Type = UIElementType.Checkbox;
        }
    }

    // RadioButton element
    public class RadioButton : UIElement
    {
        public string GroupName { get; set; }
        public bool Checked { get; set; }

        public RadioButton()
        {
            Type = UIElementType.RadioButton;
        }
    }

    // TextArea element
    public class TextArea : UIElement
    {
        public string Placeholder { get; set; }
        public int Rows { get; set; }
        public int Cols { get; set; }

        public TextArea()
        {
            Type = UIElementType.TextArea;
        }
    }

    // FileUpload element
    public class FileUpload : UIElement
    {
        public List<string> AcceptedFileTypes { get; set; } = new();

        public FileUpload()
        {
            Type = UIElementType.FileUpload;
        }
    }

    // DatePicker element
    public class DatePicker : UIElement
    {
        public DateTime MinDate { get; set; }
        public DateTime MaxDate { get; set; }
        public string DateFormat { get; set; }

        public DatePicker()
        {
            Type = UIElementType.DatePicker;
        }
    }

    // Slider element
    public class Slider : UIElement
    {
        public int MinValue { get; set; }
        public int MaxValue { get; set; }
        public int Step { get; set; }
        public int Value { get; set; }

        public Slider()
        {
            Type = UIElementType.Slider;
        }
    }

    // StaticText element
    public class StaticText : UIElement
    {
        public string Text { get; set; }

        public StaticText()
        {
            Type = UIElementType.StaticText;
        }
    }

    public enum ButtonAction
    {
        Submit,
        Next,
        Previous,
        // ... other actions as needed
    }
    // Button element
    public class Button : UIElement
    {
        public ButtonAction Action { get; set; }

        public Button()
        {
            Type = UIElementType.Button;
        }
    }
}

// EXAMPLES:

/*
function renderMap(elementData, container) {
  var mapId = "map-" + elementData.UIElementId;
  var mapDiv = $("<div>").attr("id", mapId).addClass("map-container"); // Add a CSS class for styling
  container.append(mapDiv);

  // Initialize Azure Maps
  var map = new atlas.Map(mapId, {
    center: elementData.InitialCenter.split(","),
    zoom: elementData.InitialZoom,
    style: elementData.MapStyle,
    // ... other Azure Maps options ...
  });

  // Add layers
  $.each(elementData.Layers, function(index, layerData) {
    switch (layerData.Type) {
      case "GeoJson":
        map.sources.add(new atlas.source.DataSource(null, {
          data: layerData.DataSource // URL or inline GeoJSON
        }));
        map.layers.add(new atlas.layer.PolygonLayer(layerData.DataSource, null, {
          // ... style options from layerData.StyleOptions ...
        }));
        break;
      // ... handle other layer types (Symbol, Heatmap, etc.) ...
    }
  });
}
*/


/*
// 'wizardData' is a JSON object received from your API
function renderWizard(wizardData) {
  var wizard = $("<div>").attr("id", "wizard-" + wizardData.UIElementId);
  wizard.append("<h2>" + wizardData.Label + "</h2>");

  var steps = $("<ul>");
  $.each(wizardData.Steps, function(index, step) {
    var stepItem = $("<li>").attr("id", "step-" + step.Id)
                            .html(step.Label);
    steps.append(stepItem);
  });
  wizard.append(steps);

  // Add logic for step navigation and rendering of step content
  // ...

  $("#some-container").append(wizard);
}
function renderStep(stepData) {
  var stepContent = $("<div>").attr("id", "step-content-" + stepData.UIElementId);
  $.each(stepData.UIElements, function(index, element) {
    // Call appropriate rendering function based on element type
    switch (element.Type) {
      case "TextBox":
        renderTextBox(element, stepContent);
        break;
      case "Dropdown":
        renderDropdown(element, stepContent);
        break;
      // ... other element types
    }
  });
  $("#step-" + stepData.UIElementId).append(stepContent);
}
*/

/*
function renderTextBox(elementData, container) {
  var textBox = $("<input>")
    .attr("type", "text")
    .attr("id", "textbox-" + elementData.UIElementId)
    .attr("placeholder", elementData.Placeholder)
    .attr("maxlength", elementData.MaxLength);
  var label = $("<label>")
    .attr("for", "textbox-" + elementData.UIElementId)
    .html(elementData.Label);
  container.append(label).append(textBox);
}
*/

/*
function renderInputGroup(elementData, container) {
  var inputGroup = $("<div>").addClass("input-group");
  // Add label
  var label = $("<label>").html(elementData.Label);
  inputGroup.append(label);

  // Add input element (TextBox, Dropdown, etc.)
  var inputElement = renderInputElement(elementData.InputElementType, elementData.UIElementId);
  inputGroup.append(inputElement);

  // Add add-ons (if any)
  $.each(elementData.AddOns, function(index, addon) {
    var addonElement = renderUIElement(addon); // Recursive rendering
    inputGroup.append(addonElement);
  });

  container.append(inputGroup);
}
function renderInputElement(elementType, elementId) {
  switch (elementType) {
    case "Text":
      return $("<input>").attr("type", "text").attr("id", "input-" + elementId);
    case "Email":
      return $("<input>").attr("type", "email").attr("id", "input-" + elementId);
    // ... other input types
  }
}
*/

/*
function renderCheckbox(elementData, container) {
  var checkbox = $("<input>")
    .attr("type", "checkbox")
    .attr("id", "checkbox-" + elementData.UIElementId);
  if (elementData.Checked) {
    checkbox.prop("checked", true);
  }
  var label = $("<label>")
    .attr("for", "checkbox-" + elementData.UIElementId)
    .html(elementData.Label);
  container.append(checkbox).append(label);
}
*/

/*
function renderRadioButton(elementData, container) {
  var radioButton = $("<input>")
    .attr("type", "radio")
    .attr("id", "radio-" + elementData.UIElementId)
    .attr("name", elementData.GroupName);
  if (elementData.Checked) {
    radioButton.prop("checked", true);
  }
  var label = $("<label>")
    .attr("for", "radio-" + elementData.UIElementId)
    .html(elementData.Label);
  container.append(radioButton).append(label);
}
*/

/*
function renderTextArea(elementData, container) {
  var textArea = $("<textarea>")
    .attr("id", "textarea-" + elementData.UIElementId)
    .attr("placeholder", elementData.Placeholder)
    .attr("rows", elementData.Rows)
    .attr("cols", elementData.Cols);
  var label = $("<label>")
    .attr("for", "textarea-" + elementData.UIElementId)
    .html(elementData.Label);
  container.append(label).append(textArea);
}
*/

/*
function renderFileUpload(elementData, container) {
  var fileUpload = $("<input>")
    .attr("type", "file")
    .attr("id", "file-" + elementData.UIElementId)
    .attr("accept", elementData.AcceptedFileTypes.join(",")); // Assuming AcceptedFileTypes is an array
  var label = $("<label>")
    .attr("for", "file-" + elementData.UIElementId)
    .html(elementData.Label);
  container.append(label).append(fileUpload);
}
*/

/*
function renderDatePicker(elementData, container) {
  var datePicker = $("<input>")
    .attr("type", "text")
    .attr("id", "datepicker-" + elementData.UIElementId);
  datePicker.datepicker({
    dateFormat: elementData.DateFormat,
    minDate: elementData.MinDate,
    maxDate: elementData.MaxDate
  });
  var label = $("<label>")
    .attr("for", "datepicker-" + elementData.UIElementId)
    .html(elementData.Label);
  container.append(label).append(datePicker);
}
*/

/*
function renderSlider(elementData, container) {
  var slider = $("<div>").attr("id", "slider-" + elementData.UIElementId);
  slider.slider({
    min: elementData.MinValue,
    max: elementData.MaxValue,
    step: elementData.Step,
    value: elementData.Value
  });
  var label = $("<label>")
    .attr("for", "slider-" + elementData.UIElementId)
    .html(elementData.Label);
  container.append(label).append(slider);
}
*/

/*
function renderStaticText(elementData, container) {
  var staticText = $("<p>").html(elementData.Text);
  container.append(staticText);
}
*/

/*
function renderButton(elementData, container) {
  var button = $("<button>")
    .attr("id", "button-" + elementData.UIElementId)
    .html(elementData.Label);
  // Add event handler based on button action
  switch (elementData.Action) {
    case "Submit":
      button.click(function() {
        // Handle form submission
      });
      break;
    case "Next":
      button.click(function() {
        // Navigate to the next step
      });
      break;
    // ... other actions
  }
  container.append(button);
}
*/
