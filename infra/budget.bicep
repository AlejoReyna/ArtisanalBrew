targetScope = 'subscription'

@description('Monthly budget amount in USD')
param amount int = 100

@description('Email address to notify when budget thresholds are crossed')
param notificationEmail string

@description('Start date for the budget, first of the current month (YYYY-MM-01)')
param startDate string

resource budget 'Microsoft.Consumption/budgets@2023-11-01' = {
  name: 'thiscafeteria-azure-for-students-budget'
  properties: {
    category: 'Cost'
    amount: amount
    timeGrain: 'Monthly'
    timePeriod: {
      startDate: startDate
    }
    notifications: {
      Alert20Percent: {
        enabled: true
        operator: 'GreaterThanOrEqualTo'
        threshold: 20
        contactEmails: [
          notificationEmail
        ]
        thresholdType: 'Actual'
      }
      Alert50Percent: {
        enabled: true
        operator: 'GreaterThanOrEqualTo'
        threshold: 50
        contactEmails: [
          notificationEmail
        ]
        thresholdType: 'Actual'
      }
      Alert80Percent: {
        enabled: true
        operator: 'GreaterThanOrEqualTo'
        threshold: 80
        contactEmails: [
          notificationEmail
        ]
        thresholdType: 'Actual'
      }
    }
  }
}
