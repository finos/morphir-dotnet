Feature: MainCliFeature
	Simple calculator for adding two numbers

@UsesMorphirCLI
Scenario: Run the info command
	Given the Morphir CLI is installed
	When I run with the following commandline args: info 
	Then we should get the relevant AssemblyInfo
