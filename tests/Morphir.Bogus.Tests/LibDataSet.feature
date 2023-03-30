Feature: Using LibDataSet
    In order to have meaningful test data
    As a developer
    I want to use LibDataSet
    
    Scenario: Getting n Namespaces
        Given I have a LibDataSet instance
        And n = 5
        When I call Namespaces(n)
        Then I should get a list of 5 namespaces
