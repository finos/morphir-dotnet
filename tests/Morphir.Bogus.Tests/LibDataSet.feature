Feature: Using LibDataSet
In order to have meaningful test data
As a developer
I want to use LibDataSet

    Scenario: Getting n Namespaces
        Given I have a LibDataSet instance
        And n = 5
        When I call Namespaces(n)
        Then I should get a list of 5 namespaces

    Scenario Outline: Getting Namespaces
        Given we are requesting <n> namespaces
        When I call Namespaces with n = <n>
        Then I should get a list of <expected> namespaces

        Examples: 
          | n | expected |
          | 1 | 1        |
          | 2 | 2        |
          | 3 | 3        |   
          | 4 | 4        |
          | 5 | 5        |
