# Contributing

Thank you for your interest in contributing!

The following is a set of guidelines for contributing to our project on Github. These are mostly guidelines, not rules. 

## Code of Conduct

This project and everyone participating in it is governed by the Code of Conduct.  By participating, you are expected to uphold this code. Please report unacceptable behavior to project moderators.

## Pull Requests

Please follow these guidelines when submitting pull requests (PRs):

- PRs should be manageable in size to make it easy for us to validate and integrate
- Each PR should be contained to a single fix or a single feature
- Describe the motivation for the PR 
- Describe a methodology to test and validate, if appropriate

Please ensure that the code in your PR follows a style similar to that of the project.  If you find a material discrepancy between the style followed by the project and a de facto standard style given the project language and framework, please let us know so we can amend and make our code more maintainable.

## Asking Questions

Prior to asking questions, please review closed issues and wiki pages.  If your question is not answered in either of those places, please feel free to file an issue!  This will also help us to build out documentation.

## Reporting Bugs

If you encounter an issue, please let us know!  We kindly ask that you supply the following information with your bug report when you file an issue.  Feel free to copy/paste from the below and use as a template.

--- Bug Report ---

Operating system and version: Windows 10
Framework and runtime: .NET Core 2.0
Issue encountered: The widget shifted left
Expected behavior: The widget should have shifted right
Steps to reproduce: Instantiate the widget and call .ShiftRight()
Sample code encapsulating the problem: 
```
Widget widget = new Widget();
widget.ShiftRight();
```
Exception details: [insert exception output here]
Stack trace: [if appropriate]

--- End ---

## Suggesting Enhancements

Should there be a way that you feel we could improve upon this project, please feel free to file an issue and use the template below to provide the necessary details to support the request.

Some basic guidelines for suggesting enhancements:

- Use a clear and descriptive title for the issue to identify the suggestion.
- Provide a step-by-step description of the suggested enhancement in as many details as possible.
- Provide specific examples to demonstrate the steps including copy/pasteable snippets where possible 
- Describe the current behavior and the behavior you would like to see 
- Describe the usefulness of the enhancement to yourself and potentially to others

--- Enhancement Request ---

Enhancement request title: Widgets should have a color attribute
Use case: I want to specify what color a widget is
Current behavior: Widgets don't have a color
Requested behavior: Allow me to specify a widget's color
Recommended implementation: Add a Color attribute to the Widget class
Usefulness of the enhancement: All widgets have color, and everyone has to build their own implementation to set this

--- End ---
