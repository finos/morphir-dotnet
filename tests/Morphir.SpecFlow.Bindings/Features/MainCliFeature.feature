Feature: MainCliFeature
	Simple calculator for adding two numbers

@UsesMorphirCLI
Scenario: Run the help command
	Given the Morphir CLI is installed
	When the info command is run
	Then we should get the relevant AssemblyInfo
